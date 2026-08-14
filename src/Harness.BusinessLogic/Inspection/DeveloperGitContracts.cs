using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public enum DeveloperGitIndexAction
{
    Stage,
    Unstage,
}

public sealed record DeveloperGitStateFingerprint(string Value);
public sealed record DeveloperGitPath(string Value);
public sealed record DeveloperGitIndexCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    DeveloperGitIndexAction Action,
    IReadOnlyList<DeveloperGitPath> Paths);
public sealed record DeveloperGitIndexCommandResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceGitStateView? State,
    IReadOnlyList<DeveloperGitPath> AffectedPaths,
    string? ErrorCode,
    string? Error);
public sealed record DeveloperGitPatchUnitView(
    string Id,
    DeveloperGitPath Path,
    DeveloperGitIndexAction Action,
    DeveloperGitPatchKind Kind,
    string Label,
    int? OldLine,
    int? NewLine,
    string Preview);
public enum DeveloperGitPatchKind
{
    Hunk,
    Line,
}
public sealed record DeveloperGitPatchCommand(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperGitStateFingerprint ExpectedFingerprint,
    string PatchUnitId);

public interface IDeveloperGitService
{
    ValueTask<DeveloperGitIndexCommandResult> UpdateIndexAsync(
        DeveloperGitIndexCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperGitIndexCommandResult> ApplyPatchAsync(
        DeveloperGitPatchCommand command,
        CancellationToken cancellationToken = default);
}
