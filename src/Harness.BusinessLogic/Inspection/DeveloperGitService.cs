using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Inspection;

internal sealed class DeveloperGitService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IDeveloperGitRepository repository) : IDeveloperGitService
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
