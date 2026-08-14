using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;
using System.Security.Cryptography;
using System.Text;

namespace Harness.BusinessLogic.Inspection;

internal sealed class DeveloperGitService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IDeveloperGitRepository repository,
    IWorkspaceGitInspector gitInspector) : IDeveloperGitService
{
    public async ValueTask<DeveloperGitIndexCommandResult> UpdateIndexAsync(
        DeveloperGitIndexCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace,
            cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
        {
            return new(resolution.Context, null, [],
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");
        }

        DeveloperGitIndexResult result = await repository.UpdateIndexAsync(
            new(
                resolution.RootPath,
                new(command.ExpectedFingerprint.Value),
                command.Action == DeveloperGitIndexAction.Stage
                    ? DeveloperGitIndexOperation.Stage
                    : DeveloperGitIndexOperation.Unstage,
                command.Paths.Select(path => new Harness.DataAccess.Inspection.DeveloperGitPath(
                    path.Value)).ToArray()),
            cancellationToken);
        return new(
            resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<DeveloperGitIndexCommandResult> ApplyPatchAsync(
        DeveloperGitPatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [],
                resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");

        DeveloperGitIndexResult result = await repository.ApplyPatchAsync(new(
            resolution.RootPath,
            new(command.ExpectedFingerprint.Value),
            command.PatchUnitId), cancellationToken);
        return new(
            resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<DeveloperGitDestructivePreviewResult> PreviewDestructiveAsync(
        DeveloperGitDestructivePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(null, null, resolution.ErrorCode ?? "workspace_unavailable",
                resolution.Error ?? "The workspace context is unavailable.");
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(null, null, "git_destructive_goal_context_denied",
                "Destructive Git actions are available only in the original workspace context.");

        WorkspaceGitState state = await gitInspector.InspectAsync(resolution.RootPath, cancellationToken);
        WorkspaceGitStateView view = Map(state);
        if (state.Error is not null) return new(null, view, state.ErrorCode, state.Error);
        if (!state.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal))
            return new(null, view, "git_state_stale",
                "Git state changed after it was displayed. The view was refreshed; review it and retry.");
        if (!TryValidateDestructive(command.Action, command.Paths, view, out string? error))
            return new(null, view, "git_destructive_invalid", error);

        DeveloperGitPath[] paths = command.Paths
            .DistinctBy(path => path.Value, StringComparer.Ordinal)
            .OrderBy(path => path.Value, StringComparer.Ordinal)
            .ToArray();
        string title = command.Action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
            ? $"Discard working-tree changes in {paths.Length} tracked path(s)?"
            : $"Permanently delete {paths.Length} untracked path(s)?";
        string consequence = command.Action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
            ? "The selected working-tree files will be replaced by their current index versions. Staged content stays staged."
            : "The selected untracked files or symbolic links will be deleted from disk. Tracked and staged content is not changed.";
        const string recovery = "Git does not guarantee recovery for this content. Copy anything you need before continuing.";
        var fingerprint = new DeveloperGitStateFingerprint(state.Fingerprint);
        var preview = new DeveloperGitDestructivePreviewView(
            new(PreviewId(resolution.Context, fingerprint, command.Action, paths)),
            resolution.Context,
            fingerprint,
            command.Action,
            paths,
            title,
            consequence,
            recovery,
            HasGuaranteedRecovery: false);
        return new(preview, view, null, null);
    }

    public async ValueTask<DeveloperGitIndexCommandResult> ApplyDestructiveAsync(
        DeveloperGitDestructivePreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitDestructivePreviewResult current = await PreviewDestructiveAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null),
            preview.Fingerprint,
            preview.Action,
            preview.Paths), cancellationToken);
        if (current.Preview is null || current.Error is not null)
            return new(preview.Context, current.State, [], current.ErrorCode, current.Error);
        if (!current.Preview.Id.Equals(preview.Id))
            return new(preview.Context, current.State, [], "git_destructive_preview_stale",
                "The destructive preview no longer matches. Review a new preview before continuing.");

        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], resolution.ErrorCode, resolution.Error);
        DeveloperGitIndexResult result = await repository.ApplyDestructiveAsync(new(
            resolution.RootPath,
            new(preview.Fingerprint.Value),
            preview.Action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                ? DeveloperGitDestructiveOperation.DiscardTrackedWorktree
                : DeveloperGitDestructiveOperation.DeleteUntracked,
            preview.Paths.Select(path => new Harness.DataAccess.Inspection.DeveloperGitPath(
                path.Value)).ToArray()), cancellationToken);
        return new(
            resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    private static bool TryValidateDestructive(
        DeveloperGitDestructiveAction action,
        IReadOnlyList<DeveloperGitPath> requested,
        WorkspaceGitStateView state,
        out string? error)
    {
        if (requested.Count is < 1 or > 500 || requested.Any(path => string.IsNullOrWhiteSpace(path.Value)))
        {
            error = "Select between 1 and 500 Git change paths.";
            return false;
        }
        var changes = state.Changes.ToDictionary(change => change.Path, StringComparer.Ordinal);
        foreach (DeveloperGitPath path in requested)
        {
            if (!changes.TryGetValue(path.Value, out WorkspaceGitFileChangeView? change))
            {
                error = "Every selected path must still be present in the displayed Git changes.";
                return false;
            }
            bool valid = action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                ? change.IsUnstaged && !change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal) &&
                  !change.IsConflicted
                : change.IsUnstaged && !change.IsStaged &&
                  change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal) &&
                  !change.IsConflicted;
            if (!valid)
            {
                error = action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                    ? "Discard accepts only tracked, unstaged, non-conflicted paths."
                    : "Cleanup accepts only exact untracked, unstaged, non-conflicted paths.";
                return false;
            }
        }
        error = null;
        return true;
    }

    private static string PreviewId(
        WorkbenchWorkspaceContext context,
        DeveloperGitStateFingerprint fingerprint,
        DeveloperGitDestructiveAction action,
        IReadOnlyList<DeveloperGitPath> paths)
    {
        string identity = $"{context.WorkspaceId.Value}\0{context.Scope}\0{fingerprint.Value}\0{action}\0" +
                          string.Join('\0', paths.Select(path => path.Value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static WorkspaceGitStateView Map(WorkspaceGitState state) => new(
        state.Branch,
        state.HeadSha,
        state.Changes.Select(change => new WorkspaceGitFileChangeView(
            change.Path,
            change.Status,
            change.IndexStatus,
            change.WorktreeStatus,
            change.IsStaged,
            change.IsUnstaged,
            change.IsConflicted)).ToArray(),
        state.Diff,
        state.IsTruncated,
        state.ErrorCode,
        state.Error,
        state.Fingerprint,
        state.StagedDiff,
        state.UnstagedDiff,
        MapPatchUnits(state.PatchUnits));

    internal static IReadOnlyList<DeveloperGitPatchUnitView> MapPatchUnits(
        IReadOnlyList<DeveloperGitPatchUnit>? units) => (units ?? [])
        .Select(unit => new DeveloperGitPatchUnitView(
            unit.Id,
            new(unit.Path.Value),
            unit.Direction == DeveloperGitPatchDirection.Stage
                ? DeveloperGitIndexAction.Stage
                : DeveloperGitIndexAction.Unstage,
            unit.Kind == Harness.DataAccess.Inspection.DeveloperGitPatchKind.Hunk
                ? DeveloperGitPatchKind.Hunk
                : DeveloperGitPatchKind.Line,
            unit.Label,
            unit.OldLine,
            unit.NewLine,
            unit.Preview))
        .ToArray();
}
