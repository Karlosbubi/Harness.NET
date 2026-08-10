using System.Buffers.Binary;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Goals;
using Harness.DataAccess.VisualCapture;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using StoredToolCorrelationId = Harness.DataAccess.Tools.ToolCorrelationId;

namespace Harness.BusinessLogic.Tests.VisualCapture;

public sealed class VisualCaptureServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");

    [Theory]
    [InlineData(PortalCaptureState.Cancelled, VisualCaptureOutcome.Cancelled)]
    [InlineData(PortalCaptureState.Denied, VisualCaptureOutcome.Denied)]
    [InlineData(PortalCaptureState.Unavailable, VisualCaptureOutcome.PortalUnavailable)]
    [InlineData(PortalCaptureState.Failed, VisualCaptureOutcome.PortalFailed)]
    public async Task Preserves_typed_portal_outcomes(
        PortalCaptureState portalState,
        VisualCaptureOutcome expected)
    {
        VisualCaptureService service = CreateService(new Portal(portalState), new Artifacts());

        VisualCaptureResult result = await service.CaptureAsync(Request());

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Rejects_stale_requests_before_opening_the_portal()
    {
        Portal portal = new(PortalCaptureState.Succeeded);
        VisualCaptureService service = CreateService(portal, new Artifacts());

        VisualCaptureResult result = await service.CaptureAsync(Request() with
            { RequestedAt = Now.AddMinutes(-3) });

        Assert.Equal(VisualCaptureOutcome.StaleRequest, result.Outcome);
        Assert.Equal(0, portal.CaptureCalls);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task Stores_one_exact_frame_with_explicit_scale_and_private_remote_disclosure(
        double uiScale)
    {
        Artifacts artifacts = new();
        VisualCaptureService service = CreateService(
            new Portal(PortalCaptureState.Succeeded), artifacts);

        VisualCaptureResult captured = await service.CaptureAsync(Request() with
            { UiScale = new(uiScale) });
        VisualCaptureInspectionResult remote = await service.InspectAsync(
            new("goal-a"), captured.Capture!.Id, VisualCaptureModelAccess.Remote);
        VisualCaptureInspectionResult local = await service.InspectAsync(
            new("goal-a"), captured.Capture.Id, VisualCaptureModelAccess.Local);

        Assert.Equal(VisualCaptureOutcome.Succeeded, captured.Outcome);
        Assert.Equal(VisualCaptureIdentityState.Unavailable, captured.Capture.IdentityState);
        Assert.Equal(VisualCaptureScaleState.ApplicationSupplied, captured.Capture.ScaleState);
        Assert.Equal(uiScale, captured.Capture.UiScale?.Value);
        Assert.Equal(VisualCaptureOutcome.PolicyRejected, remote.Outcome);
        Assert.Equal(VisualCaptureOutcome.Succeeded, local.Outcome);
        Assert.Equal(Convert.ToBase64String(Png()), local.Content?.Content.Base64);
    }

    [Fact]
    public async Task Reports_multi_target_portal_capability_without_inventing_display_identity()
    {
        Portal portal = new(PortalCaptureState.Succeeded)
        {
            Availability = new(true, 3, 1 | 2 | 4 | 8, null, null),
        };
        VisualCaptureSettingsSnapshot settings = await CreateService(portal, new Artifacts())
            .GetSettingsAsync();

        Assert.Contains(VisualCaptureTarget.Window, settings.Availability.AvailableTargets);
        Assert.Contains(VisualCaptureTarget.ActiveWindow, settings.Availability.AvailableTargets);
    }

    private static VisualCaptureRequest Request() => new(
        new("goal-a"), new ToolCorrelationId(Guid.NewGuid().ToString("N")),
        VisualCaptureInitiator.Developer, new("Verify rendered board"), new("Harness.NET"),
        VisualCaptureTarget.UserSelection, Now);

    private static VisualCaptureService CreateService(Portal portal, Artifacts artifacts) => new(
        new Goals(), new Workspaces(), portal, new Images(), artifacts,
        new Preferences(), new Evidence(), new FixedTimeProvider());

    private static byte[] Png()
    {
        byte[] bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 2);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 3);
        return bytes;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Portal(PortalCaptureState state) : IVisualCapturePortal
    {
        public int CaptureCalls { get; private set; }
        public PortalCaptureAvailability Availability { get; init; } = new(true, 3, 15, null, null);
        public ValueTask<PortalCaptureAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Availability);
        public ValueTask<PortalCaptureResult> CaptureAsync(PortalCaptureRequest request, CancellationToken cancellationToken = default)
        {
            CaptureCalls++;
            return ValueTask.FromResult(new PortalCaptureResult(state,
                state is PortalCaptureState.Succeeded ? new Uri("file:///portal/frame.png") : null,
                3, 15, state.ToString(), state.ToString()));
        }
    }

    private sealed class Images : IVisualCaptureImageSourceReader
    {
        public ValueTask<PortalImageReadResult> ReadAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PortalImageReadResult(PortalImageReadState.Succeeded, Png(), null, null));
    }

    private sealed class Preferences : IVisualCapturePreferenceStore
    {
        private StoredVisualCapturePreference value = new(true, 5 * 1024 * 1024, 7, 20, false);
        public ValueTask<StoredVisualCapturePreference> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(value);
        public ValueTask<StoredVisualCapturePreference> SaveAsync(StoredVisualCapturePreference preference, CancellationToken cancellationToken = default)
        {
            value = preference;
            return ValueTask.FromResult(value);
        }
    }

    private sealed class Artifacts : IVisualCaptureArtifactStore
    {
        private StoredVisualCaptureContent? content;
        public ValueTask<StoredVisualCapture> StoreAsync(StoredVisualCaptureWrite write, CancellationToken cancellationToken = default)
        {
            StoredVisualCapture stored = write.Capture with { ArtifactFileName = write.Capture.Id.Value + ".png" };
            content = new(stored, write.Content);
            return ValueTask.FromResult(stored);
        }
        public ValueTask<IReadOnlyList<StoredVisualCapture>> ListAsync(string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredVisualCapture>>(content is null ? [] : [content.Capture]);
        public ValueTask<StoredVisualCaptureContent?> ReadAsync(string goalId, StoredVisualCaptureId captureId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(content?.Capture.Id == captureId ? content : null);
        public ValueTask<bool> DeleteAsync(string goalId, StoredVisualCaptureId captureId, CancellationToken cancellationToken = default)
        {
            content = null;
            return ValueTask.FromResult(true);
        }
        public ValueTask<VisualCaptureCleanupResult> CleanupAsync(VisualCaptureRetentionPolicy policy, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureCleanupResult(0, 0, 0));
    }

    private sealed class Goals : IGoalStore
    {
        public ValueTask<StoredGoal?> GetAsync(string goalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<StoredGoal?>(new(goalId, "workspace-a", "Goal", "Goal", 2, null, "Approved", Now, Now));
        public ValueTask<StoredGoal> CreateAsync(StoredGoal goal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit, long? remoteBudgetMicrousd, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(string extensionId, string goalId, long? expectedBudgetMicrousd, long newBudgetMicrousd, string reason, DateTimeOffset approvedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlan?> GetCurrentPlanAsync(string goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> SavePlanAsync(StoredPlan plan, string expectedGoalState, string nextGoalState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(StoredApproval approval, StoredGoalWorktree? worktree, string expectedGoalState, string expectedPlanState, string nextGoalState, string nextPlanState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(string goalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Workspaces : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<RegisteredWorkspace?>(new("workspace-a", "/workspace", "Workspace", "App.slnx", true, true, "main", false, Now, Now));
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection, string entryPoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool isTrusted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Evidence : IToolEvidenceStore
    {
        public ValueTask<StoredToolCallStart> StartAsync(StoredToolCall toolCall, CancellationToken cancellationToken = default) => ValueTask.FromResult(new StoredToolCallStart(toolCall, true));
        public ValueTask<StoredToolCall> CompleteAsync(ToolCallId toolCallId, ToolCallState expectedState, ToolCallState nextState, string resultJson, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => ValueTask.FromResult(new StoredToolCall(toolCallId, "goal-a", new StoredToolCorrelationId("completed"), Harness.DataAccess.Evidence.ToolKind.VisualCapture, "{}", nextState, resultJson, Now, completedAt));
        public ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(string goalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<StoredToolCall>>([]);
    }
}
