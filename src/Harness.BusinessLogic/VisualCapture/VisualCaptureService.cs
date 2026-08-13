using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Privacy;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Tools;
using Harness.DataAccess.VisualCapture;
using Harness.DataAccess.Workspaces;
using StoredToolCorrelationId = Harness.DataAccess.Tools.ToolCorrelationId;

namespace Harness.BusinessLogic.VisualCapture;

internal sealed class VisualCaptureService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IVisualCapturePortal portal,
    IVisualCaptureImageSourceReader imageReader,
    IVisualCaptureArtifactStore artifactStore,
    IVisualCapturePreferenceStore preferenceStore,
    IToolEvidenceStore evidenceStore,
    ISensitiveDisplayGuard sensitiveDisplayGuard,
    TimeProvider timeProvider) : IVisualCaptureService
{
    private const long MinimumBytes = 1024 * 1024;
    private const long MaximumBytes = 16 * 1024 * 1024;
    private const long MaximumPixels = 64L * 1024 * 1024;
    private static readonly TimeSpan MaximumRequestAge = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim artifactGate = new(1, 1);

    public async ValueTask<VisualCaptureSettingsSnapshot> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        StoredVisualCapturePreference stored = await preferenceStore.GetAsync(cancellationToken);
        PortalCaptureAvailability availability = await portal.GetAvailabilityAsync(cancellationToken);
        return new(Map(stored), Map(availability),
            "Private XDG state; excluded from repositories and backups.");
    }

    public async ValueTask<VisualCaptureSettingsResult> SaveSettingsAsync(
        VisualCapturePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        string? error = Validate(preferences);
        if (error is not null)
        {
            return new(null, "invalid_capture_preferences", error);
        }
        StoredVisualCapturePreference stored = await preferenceStore.SaveAsync(
            new(
                preferences.IsEnabled,
                preferences.MaximumBytes.Value,
                preferences.RetentionDays.Value,
                preferences.MaximumPerGoal.Value,
                preferences.AllowRemoteModelAccess),
            cancellationToken);
        await CleanupAsync(cancellationToken);
        return new(
            new(Map(stored), Map(await portal.GetAvailabilityAsync(cancellationToken)),
                "Private XDG state; excluded from repositories and backups."),
            null,
            null);
    }

    public async ValueTask<VisualCaptureResult> CaptureAsync(
        VisualCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        string? invalid = Validate(request);
        if (invalid is not null)
        {
            return Failure(VisualCaptureOutcome.InvalidRequest, "invalid_capture_request", invalid);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (request.RequestedAt < now - MaximumRequestAge || request.RequestedAt > now.AddMinutes(1))
        {
            return Failure(VisualCaptureOutcome.StaleRequest, "capture_request_stale",
                "The capture request is outside the accepted time window.");
        }

        StoredVisualCapturePreference preference = await preferenceStore.GetAsync(cancellationToken);
        if (!preference.IsEnabled)
        {
            return Failure(VisualCaptureOutcome.Disabled, "capture_disabled",
                "Visual capture is disabled in Settings.");
        }

        if (!sensitiveDisplayGuard.TryBeginVisualCapture(out ISensitiveDisplayLease? captureLease))
        {
            return Failure(VisualCaptureOutcome.PolicyRejected,
                "sensitive_content_visible",
                "Hide the revealed sensitive value before requesting visual capture.");
        }
        using ISensitiveDisplayLease activeCapture = captureLease!;

        (StoredGoal? goal, RegisteredWorkspace? workspace, string? scopeError) =
            await ResolveScopeAsync(request.GoalId, cancellationToken);
        if (scopeError is not null)
        {
            return Failure(VisualCaptureOutcome.PolicyRejected, "capture_scope_denied", scopeError);
        }

        StoredToolCallStart evidence = await StartEvidenceAsync(request, cancellationToken);
        if (!evidence.WasCreated)
        {
            return Failure(VisualCaptureOutcome.InvalidRequest, "duplicate_correlation",
                "This goal already has a tool call with that correlation identifier.");
        }

        VisualCaptureResult result;
        try
        {
            await artifactGate.WaitAsync(cancellationToken);
            try
            {
                await artifactStore.CleanupAsync(new(
                    preference.RetentionDays,
                    preference.MaximumCapturesPerGoal,
                    now), cancellationToken);
            }
            finally
            {
                artifactGate.Release();
            }
            PortalCaptureResult portalResult = await portal.CaptureAsync(new(
                request.ParentWindow is null ? null : new(request.ParentWindow.Value),
                Map(request.Target)), cancellationToken);
            if (portalResult.State is not PortalCaptureState.Succeeded || portalResult.ImageUri is null)
            {
                result = PortalFailure(portalResult);
            }
            else
            {
                PortalImageReadResult read = await imageReader.ReadAsync(
                    portalResult.ImageUri,
                    preference.MaximumBytes,
                    cancellationToken);
                if (read.State is not PortalImageReadState.Succeeded)
                {
                    result = ReadFailure(read);
                }
                else if (!TryReadImage(read.Content.Span, out string mediaType,
                             out int width, out int height) ||
                         (long)width * height > MaximumPixels)
                {
                    result = Failure(VisualCaptureOutcome.InvalidImage,
                        "capture_image_invalid",
                        "The portal result is not a supported bounded PNG or JPEG image.");
                }
                else
                {
                    string id = Guid.NewGuid().ToString("N");
                    string sha256 = Convert.ToHexStringLower(SHA256.HashData(read.Content.Span));
                    await artifactGate.WaitAsync(cancellationToken);
                    StoredVisualCapture stored;
                    try
                    {
                        stored = await artifactStore.StoreAsync(new(
                            new(
                                new(id),
                                goal!.Id,
                                workspace!.Id,
                                request.Initiator.ToString(),
                                request.RelatedAction.Value,
                                request.ApplicationIdentity.Value,
                                MapStored(request.Target),
                                StoredVisualCaptureIdentityState.Unavailable,
                                WindowIdentity: null,
                                DisplayIdentity: null,
                                request.UiScale is null
                                    ? StoredVisualCaptureScaleState.Unavailable
                                    : StoredVisualCaptureScaleState.ApplicationSupplied,
                                request.UiScale?.Value,
                                width,
                                height,
                                mediaType,
                                read.Content.Length,
                                sha256,
                                now,
                                ArtifactFileName: string.Empty),
                            read.Content), cancellationToken);
                    }
                    finally
                    {
                        artifactGate.Release();
                    }
                    result = new(VisualCaptureOutcome.Succeeded, Map(stored), null, null);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = Failure(VisualCaptureOutcome.Cancelled, "capture_cancelled",
                "The capture was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or
                                          UnauthorizedAccessException or InvalidOperationException)
        {
            result = Failure(VisualCaptureOutcome.StorageFailed, "capture_storage_failed",
                exception.Message);
        }

        await evidenceStore.CompleteAsync(
            evidence.ToolCall.Id,
            ToolCallState.Running,
            result.Outcome is VisualCaptureOutcome.Succeeded
                ? ToolCallState.Succeeded
                : result.Outcome is VisualCaptureOutcome.Cancelled
                    ? ToolCallState.Cancelled
                    : ToolCallState.Failed,
            JsonSerializer.Serialize(result),
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return result;
    }

    public async ValueTask<VisualCaptureListResult> ListAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        (_, _, string? error) = await ResolveScopeAsync(goalId, cancellationToken);
        if (error is not null)
        {
            return new([], "capture_scope_denied", error);
        }
        await artifactGate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<StoredVisualCapture> stored = await artifactStore.ListAsync(
                goalId.Value, cancellationToken);
            return new(stored.Select(Map).ToArray(), null, null);
        }
        finally
        {
            artifactGate.Release();
        }
    }

    public async ValueTask<VisualCaptureInspectionResult> InspectAsync(
        GoalId goalId,
        VisualCaptureId captureId,
        VisualCaptureModelAccess access,
        CancellationToken cancellationToken = default)
    {
        if (goalId is null || captureId is null ||
            !Guid.TryParseExact(captureId.Value, "N", out _) || !Enum.IsDefined(access))
        {
            return InspectionFailure(VisualCaptureOutcome.InvalidRequest,
                "invalid_capture_inspection", "A valid goal, capture, and access mode are required.");
        }
        (_, _, string? error) = await ResolveScopeAsync(goalId, cancellationToken);
        if (error is not null)
        {
            return InspectionFailure(VisualCaptureOutcome.PolicyRejected,
                "capture_scope_denied", error);
        }
        StoredVisualCapturePreference preference = await preferenceStore.GetAsync(cancellationToken);
        if (access is VisualCaptureModelAccess.Remote && !preference.AllowRemoteModelAccess)
        {
            return InspectionFailure(VisualCaptureOutcome.PolicyRejected,
                "remote_capture_access_denied",
                "Remote-model access to screenshots is disabled in Settings.");
        }
        await artifactGate.WaitAsync(cancellationToken);
        StoredVisualCaptureContent? stored;
        try
        {
            stored = await artifactStore.ReadAsync(
                goalId.Value, new(captureId.Value), cancellationToken);
        }
        finally
        {
            artifactGate.Release();
        }
        return stored is null
            ? InspectionFailure(VisualCaptureOutcome.NotFound, "capture_not_found",
                "The capture is missing, expired, deleted, or failed integrity validation.")
            : new(
                VisualCaptureOutcome.Succeeded,
                new(Map(stored.Capture), new(Convert.ToBase64String(stored.Content.Span))),
                null,
                null);
    }

    public async ValueTask<bool> DeleteAsync(
        GoalId goalId,
        VisualCaptureId captureId,
        CancellationToken cancellationToken = default)
    {
        (_, _, string? error) = await ResolveScopeAsync(goalId, cancellationToken);
        if (error is not null)
        {
            return false;
        }
        await artifactGate.WaitAsync(cancellationToken);
        try
        {
            return await artifactStore.DeleteAsync(
                goalId.Value, new(captureId.Value), cancellationToken);
        }
        finally
        {
            artifactGate.Release();
        }
    }

    public async ValueTask CleanupAsync(CancellationToken cancellationToken = default)
    {
        StoredVisualCapturePreference preference = await preferenceStore.GetAsync(cancellationToken);
        await artifactGate.WaitAsync(cancellationToken);
        try
        {
            await artifactStore.CleanupAsync(new(
                preference.RetentionDays,
                preference.MaximumCapturesPerGoal,
                timeProvider.GetUtcNow()), cancellationToken);
        }
        finally
        {
            artifactGate.Release();
        }
    }

    private async ValueTask<(StoredGoal?, RegisteredWorkspace?, string?)> ResolveScopeAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        StoredGoal? goal = goalId is null
            ? null
            : await goalStore.GetAsync(goalId.Value, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal is null)
        {
            return (null, workspace, "The goal does not exist.");
        }
        if (workspace is null || workspace.Id != goal.WorkspaceId)
        {
            return (goal, workspace, "The goal workspace must be active.");
        }
        if (!workspace.IsTrusted)
        {
            return (goal, workspace, "The goal workspace must be trusted.");
        }
        return (goal, workspace, null);
    }

    private async ValueTask<StoredToolCallStart> StartEvidenceAsync(
        VisualCaptureRequest request,
        CancellationToken cancellationToken) => await evidenceStore.StartAsync(new(
            new(Guid.NewGuid().ToString("N")),
            request.GoalId.Value,
            new StoredToolCorrelationId(request.CorrelationId.Value),
            Harness.DataAccess.Evidence.ToolKind.VisualCapture,
            JsonSerializer.Serialize(new
            {
                request.Initiator,
                relatedAction = request.RelatedAction.Value,
                applicationIdentity = request.ApplicationIdentity.Value,
                request.Target,
                request.RequestedAt,
                hasParentWindow = request.ParentWindow is not null,
                uiScale = request.UiScale?.Value,
            }),
            ToolCallState.Running,
            ResultJson: null,
            timeProvider.GetUtcNow(),
            CompletedAt: null), cancellationToken);

    private static string? Validate(VisualCapturePreferences preferences)
    {
        if (preferences is null || preferences.MaximumBytes is null ||
            preferences.RetentionDays is null || preferences.MaximumPerGoal is null)
        {
            return "Complete visual-capture preferences are required.";
        }
        if (preferences.MaximumBytes.Value is < MinimumBytes or > MaximumBytes)
        {
            return "Maximum encoded image size must be from 1 through 16 MiB.";
        }
        if (preferences.RetentionDays.Value is < 1 or > 90)
        {
            return "Retention must be from 1 through 90 days.";
        }
        return preferences.MaximumPerGoal.Value is < 1 or > 100
            ? "Maximum captures per goal must be from 1 through 100."
            : null;
    }

    private static string? Validate(VisualCaptureRequest request)
    {
        if (request is null || request.GoalId is null ||
            string.IsNullOrWhiteSpace(request.GoalId.Value) || request.CorrelationId is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId.Value) ||
            request.CorrelationId.Value.Length > 128 || !Enum.IsDefined(request.Initiator) ||
            !Enum.IsDefined(request.Target) || request.RelatedAction is null ||
            string.IsNullOrWhiteSpace(request.RelatedAction.Value) ||
            request.RelatedAction.Value.Length > 512 || request.ApplicationIdentity is null ||
            string.IsNullOrWhiteSpace(request.ApplicationIdentity.Value) ||
            request.ApplicationIdentity.Value.Length > 128 ||
            request.ParentWindow?.Value.Length > 512 ||
            request.UiScale is { Value: < 0.5 or > 8 })
        {
            return "The capture request contains invalid or oversized values.";
        }
        return null;
    }

    private static bool TryReadImage(
        ReadOnlySpan<byte> content,
        out string mediaType,
        out int width,
        out int height)
    {
        mediaType = string.Empty;
        width = 0;
        height = 0;
        if (content.Length >= 24 && content[..8].SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(content.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(content.Slice(20, 4));
            mediaType = "image/png";
            return width is > 0 and <= 16384 && height is > 0 and <= 16384;
        }

        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8)
        {
            return false;
        }
        int offset = 2;
        while (offset + 9 < content.Length)
        {
            if (content[offset] != 0xFF)
            {
                return false;
            }
            byte marker = content[offset + 1];
            offset += 2;
            if (marker is 0xD8 or 0xD9)
            {
                continue;
            }
            if (offset + 2 > content.Length)
            {
                return false;
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset, 2));
            if (length < 2 || offset + length > content.Length)
            {
                return false;
            }
            if (length >= 7 && marker is (0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
                0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF))
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 5, 2));
                mediaType = "image/jpeg";
                return width > 0 && height > 0;
            }
            offset += length;
        }
        return false;
    }

    private static VisualCaptureResult PortalFailure(PortalCaptureResult result) => result.State switch
    {
        PortalCaptureState.Cancelled => Failure(VisualCaptureOutcome.Cancelled,
            result.ErrorCode ?? "capture_cancelled", result.Error ?? "The capture was cancelled."),
        PortalCaptureState.Denied => Failure(VisualCaptureOutcome.Denied,
            result.ErrorCode ?? "capture_denied", result.Error ?? "The capture was denied."),
        PortalCaptureState.Unavailable => Failure(VisualCaptureOutcome.PortalUnavailable,
            result.ErrorCode ?? "portal_unavailable", result.Error ?? "The portal is unavailable."),
        _ => Failure(VisualCaptureOutcome.PortalFailed,
            result.ErrorCode ?? "portal_capture_failed", result.Error ?? "The portal capture failed."),
    };

    private static VisualCaptureResult ReadFailure(PortalImageReadResult result) =>
        Failure(result.State is PortalImageReadState.TooLarge
                ? VisualCaptureOutcome.SizeRejected
                : VisualCaptureOutcome.InvalidImage,
            result.ErrorCode ?? "capture_read_failed",
            result.Error ?? "The portal image could not be read.");

    private static VisualCaptureResult Failure(
        VisualCaptureOutcome outcome,
        string code,
        string error) => new(outcome, null, code, error);

    private static VisualCaptureInspectionResult InspectionFailure(
        VisualCaptureOutcome outcome,
        string code,
        string error) => new(outcome, null, code, error);

    private static VisualCapturePreferences Map(StoredVisualCapturePreference item) => new(
        item.IsEnabled,
        new(item.MaximumBytes),
        new(item.RetentionDays),
        new(item.MaximumCapturesPerGoal),
        item.AllowRemoteModelAccess);

    private static VisualCaptureAvailability Map(PortalCaptureAvailability item) => new(
        item.IsAvailable,
        item.InterfaceVersion,
        AvailableTargets(item.InterfaceVersion, item.AvailableTargets),
        item.ErrorCode,
        item.Error);

    private static IReadOnlyList<VisualCaptureTarget> AvailableTargets(uint version, uint targets) =>
        version < 3
            ? [VisualCaptureTarget.UserSelection]
            : new[]
            {
                (Value: 1u, Target: VisualCaptureTarget.Screen),
                (Value: 2u, Target: VisualCaptureTarget.Window),
                (Value: 4u, Target: VisualCaptureTarget.Area),
                (Value: 8u, Target: VisualCaptureTarget.ActiveWindow),
            }.Where(item => (targets & item.Value) != 0)
                .Select(item => item.Target)
                .Prepend(VisualCaptureTarget.UserSelection)
                .ToArray();

    private static VisualCaptureView Map(StoredVisualCapture item) => new(
        new(item.Id.Value),
        new(item.GoalId),
        item.WorkspaceId,
        Enum.Parse<VisualCaptureInitiator>(item.Initiator),
        new(item.RelatedAction),
        new(item.ApplicationIdentity),
        Map(item.Target),
        item.IdentityState is StoredVisualCaptureIdentityState.Unavailable
            ? VisualCaptureIdentityState.Unavailable
            : VisualCaptureIdentityState.ApplicationSupplied,
        item.WindowIdentity,
        item.DisplayIdentity,
        item.ScaleState is StoredVisualCaptureScaleState.Unavailable
            ? VisualCaptureScaleState.Unavailable
            : VisualCaptureScaleState.ApplicationSupplied,
        item.UiScale is null ? null : new(item.UiScale.Value),
        new(item.PixelWidth, item.PixelHeight),
        new(item.MediaType),
        new(item.Bytes),
        new(item.Sha256),
        item.CreatedAt);

    private static PortalCaptureTarget Map(VisualCaptureTarget target) => target switch
    {
        VisualCaptureTarget.UserSelection => PortalCaptureTarget.UserSelection,
        VisualCaptureTarget.Screen => PortalCaptureTarget.Screen,
        VisualCaptureTarget.Window => PortalCaptureTarget.Window,
        VisualCaptureTarget.Area => PortalCaptureTarget.Area,
        VisualCaptureTarget.ActiveWindow => PortalCaptureTarget.ActiveWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static StoredVisualCaptureTarget MapStored(VisualCaptureTarget target) => target switch
    {
        VisualCaptureTarget.UserSelection => StoredVisualCaptureTarget.UserSelection,
        VisualCaptureTarget.Screen => StoredVisualCaptureTarget.Screen,
        VisualCaptureTarget.Window => StoredVisualCaptureTarget.Window,
        VisualCaptureTarget.Area => StoredVisualCaptureTarget.Area,
        VisualCaptureTarget.ActiveWindow => StoredVisualCaptureTarget.ActiveWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static VisualCaptureTarget Map(StoredVisualCaptureTarget target) => target switch
    {
        StoredVisualCaptureTarget.UserSelection => VisualCaptureTarget.UserSelection,
        StoredVisualCaptureTarget.Screen => VisualCaptureTarget.Screen,
        StoredVisualCaptureTarget.Window => VisualCaptureTarget.Window,
        StoredVisualCaptureTarget.Area => VisualCaptureTarget.Area,
        StoredVisualCaptureTarget.ActiveWindow => VisualCaptureTarget.ActiveWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
