using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Inspection;

internal sealed partial class DeveloperGitService
{
    public async ValueTask<DeveloperGitHistoryPageView> InspectHistoryAsync(
        DeveloperGitHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, request.Path, [], null,
                resolution.ErrorCode, resolution.Error);
        DeveloperGitHistoryPage page = await Task.Run(async () =>
            await repository.InspectHistoryAsync(new(
                resolution.RootPath,
                request.Path is null ? null : new Harness.DataAccess.Inspection.DeveloperGitPath(
                    request.Path.Value),
                request.Cursor is null ? null : new Harness.DataAccess.Inspection.DeveloperGitHistoryCursor(
                    request.Cursor.Value),
                request.MaximumResults), cancellationToken), cancellationToken);
        return new(resolution.Context, page.State is null ? null : Map(page.State), request.Path,
            page.Commits.Select(commit => new DeveloperGitHistoryCommitView(
                new(commit.Sha.Value),
                commit.Parents.Select(parent => new DeveloperGitCommitSha(parent.Value)).ToArray(),
                commit.AuthorName, commit.AuthoredAt, commit.Subject, commit.References)).ToArray(),
            page.NextCursor is null ? null : new(page.NextCursor.Value), page.ErrorCode, page.Error);
    }

    public async ValueTask<DeveloperGitCommitDetailResult> InspectCommitAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperGitCommitSha commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(commit);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null, resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitCommitDetailResult result = await Task.Run(async () =>
            await repository.InspectCommitAsync(
                resolution.RootPath, new(commit.Value), cancellationToken), cancellationToken);
        DeveloperGitCommitDetailView? detail = result.Detail is null ? null : new(
            new(result.Detail.Sha.Value),
            result.Detail.Parents.Select(parent => new DeveloperGitCommitSha(parent.Value)).ToArray(),
            result.Detail.AuthorName, result.Detail.AuthorEmail, result.Detail.AuthoredAt,
            result.Detail.CommitterName, result.Detail.CommitterEmail, result.Detail.CommittedAt,
            result.Detail.Message, result.Detail.MessageIsTruncated, result.Detail.References,
            result.Detail.ParentDiffs.Select(diff => new DeveloperGitCommitParentDiffView(
                diff.Parent is null ? null : new(diff.Parent.Value),
                diff.Paths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
                diff.Patch, diff.IsTruncated)).ToArray());
        return new(resolution.Context, result.State is null ? null : Map(result.State),
            detail, result.ErrorCode, result.Error);
    }

    public async ValueTask<DeveloperGitBlamePageView> InspectBlameAsync(
        DeveloperGitBlameRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, request.Path, [], null,
                resolution.ErrorCode, resolution.Error);
        DeveloperGitBlamePage page = await Task.Run(async () =>
            await repository.InspectBlameAsync(new(
                resolution.RootPath, new(request.Path.Value), request.StartLine, request.MaximumLines),
                cancellationToken), cancellationToken);
        return new(resolution.Context, page.State is null ? null : Map(page.State), request.Path,
            page.Lines.Select(line => new DeveloperGitBlameLineView(
                line.LineNumber, new(line.Commit.Value), line.AuthorName, line.AuthoredAt,
                new(line.OriginalPath.Value), line.OriginalLineNumber, line.Text)).ToArray(),
            page.NextStartLine, page.ErrorCode, page.Error);
    }

    public async ValueTask<DeveloperGitConflictInspectionResult> InspectConflictsAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], false,
                resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitConflictInspection inspection =
            await Task.Run(async () => await repository.InspectConflictsAsync(
                resolution.RootPath, cancellationToken), cancellationToken);
        return new(resolution.Context,
            inspection.State is null ? null : Map(inspection.State),
            inspection.Conflicts.Select(MapConflictSummary).ToArray(),
            inspection.IsTruncated,
            inspection.ErrorCode,
            inspection.Error);
    }

    public async ValueTask<DeveloperGitConflictDocumentResult> InspectConflictAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperGitPath path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(path);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null,
                resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitConflictDocumentResult result =
            await Task.Run(async () => await repository.InspectConflictAsync(
                resolution.RootPath,
                new Harness.DataAccess.Inspection.DeveloperGitPath(path.Value),
                cancellationToken), cancellationToken);
        return MapConflictDocument(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitConflictDocumentResult> SaveConflictResultAsync(
        DeveloperGitConflictSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, null,
                resolution.ErrorCode, resolution.Error);
        Harness.DataAccess.Inspection.DeveloperGitConflictDocumentResult result =
            await Task.Run(async () => await repository.SaveConflictResultAsync(new(
                resolution.RootPath,
                new Harness.DataAccess.Inspection.DeveloperGitStateFingerprint(
                    command.ExpectedFingerprint.Value),
                new Harness.DataAccess.Inspection.DeveloperGitPath(command.Path.Value),
                new Harness.DataAccess.Inspection.DeveloperGitContentHash(
                    command.ExpectedResultHash.Value),
                command.Result), cancellationToken), cancellationToken);
        return MapConflictDocument(resolution.Context, result);
    }

    public async ValueTask<DeveloperGitIndexCommandResult> StageConflictResultAsync(
        DeveloperGitConflictStageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            command.Workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [],
                resolution.ErrorCode, resolution.Error);
        DeveloperGitIndexResult result = await Task.Run(async () =>
            await repository.StageConflictResultAsync(new(
                resolution.RootPath,
                new Harness.DataAccess.Inspection.DeveloperGitStateFingerprint(
                    command.ExpectedFingerprint.Value),
                new Harness.DataAccess.Inspection.DeveloperGitPath(command.Path.Value),
                new Harness.DataAccess.Inspection.DeveloperGitContentHash(
                    command.ExpectedResultHash.Value)), cancellationToken), cancellationToken);
        return new(resolution.Context,
            result.State is null ? null : Map(result.State),
            result.AffectedPaths.Select(path => new DeveloperGitPath(path.Value)).ToArray(),
            result.ErrorCode,
            result.Error);
    }

    public async ValueTask<DeveloperGitRemoteInspectionResult> InspectRemotesAsync(
        WorkbenchWorkspaceRequest workspace,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(workspace, cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return new(resolution.Context, null, [], null, null, null, null, null, null, null,
                resolution.ErrorCode, resolution.Error);
        if (resolution.Context.Scope != WorkbenchWorkspaceScope.OriginalWorkspace)
            return new(resolution.Context, null, [], null, null, null, null, null, null, null,
                "git_remote_goal_context_denied",
                "Developer remote synchronization is available only in the original workspace.");
        return MapRemote(resolution.Context,
            await repository.InspectRemotesAsync(resolution.RootPath, cancellationToken));
    }

    public async ValueTask<DeveloperGitRemotePreviewResult> PreviewRemoteAsync(
        DeveloperGitRemotePreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DeveloperGitRemoteInspectionResult inspection = await InspectRemotesAsync(
            command.Workspace, cancellationToken);
        if (inspection.State is null || inspection.Error is not null)
            return new(null, inspection, inspection.ErrorCode, inspection.Error);
        if (!inspection.State.Fingerprint.Equals(command.ExpectedFingerprint.Value, StringComparison.Ordinal))
            return new(null, inspection, "git_state_stale",
                "Git references or working state changed after display. Refresh and retry.");
        if (!inspection.Remotes.Any(remote => remote.Name == command.Remote))
            return new(null, inspection, "git_remote_missing", "Select a configured Git remote.");
        if (string.IsNullOrWhiteSpace(command.Source.Value) ||
            string.IsNullOrWhiteSpace(command.Destination.Value))
            return new(null, inspection, "git_remote_reference_invalid",
                "Choose explicit source and destination branch names.");
        if ((command.Action is DeveloperGitRemoteAction.PullMerge or DeveloperGitRemoteAction.PullRebase) &&
            inspection.State.Changes.Count > 0)
            return new(null, inspection, "git_pull_dirty",
                "Commit or stash working changes before integrating fetched commits.");
        if ((command.Action is DeveloperGitRemoteAction.PullMerge or DeveloperGitRemoteAction.PullRebase) &&
            (inspection.LocalBranch?.Value != command.Source.Value ||
             inspection.UpstreamRemote != command.Remote ||
             inspection.UpstreamBranch?.Value != command.Destination.Value))
            return new(null, inspection, "git_pull_upstream_mismatch",
                "Integration must target the displayed current branch and its configured upstream.");
        if (command.Action is DeveloperGitRemoteAction.PullMerge && inspection.Behind is > 0 &&
            inspection.Ahead is > 0)
            return new(null, inspection, "git_pull_not_fast_forward",
                "The branch has diverged. Choose the explicit rebase integration policy or reconcile manually.");
        if (command.Action != DeveloperGitRemoteAction.Push &&
            command.PushPolicy == DeveloperGitPushPolicy.ForceWithLease)
            return new(null, inspection, "git_force_policy_invalid",
                "Force-with-lease applies only to push.");
        if (command.Action == DeveloperGitRemoteAction.Push &&
            command.PushPolicy == DeveloperGitPushPolicy.ForceWithLease &&
            inspection.RemoteTrackingSha is null)
            return new(null, inspection, "git_force_lease_unknown",
                "Fetch the destination first so force-with-lease can bind its observed commit.");
        if (command.Action == DeveloperGitRemoteAction.Push &&
            command.PushPolicy == DeveloperGitPushPolicy.ForceWithLease &&
            (inspection.UpstreamRemote != command.Remote ||
             inspection.UpstreamBranch?.Value != command.Destination.Value))
            return new(null, inspection, "git_force_lease_destination_mismatch",
                "Force-with-lease must target the displayed upstream branch whose commit was observed.");

        string consequence = command.Action switch
        {
            DeveloperGitRemoteAction.Fetch =>
                $"Fetch {command.Remote.Value}/{command.Source.Value} into the local remote-tracking ref for {command.Destination.Value}. No working files are integrated.",
            DeveloperGitRemoteAction.PullMerge =>
                $"Fast-forward the current local branch from the already-fetched {command.Remote.Value}/{command.Destination.Value} tracking ref.",
            DeveloperGitRemoteAction.PullRebase =>
                $"Rebase the current local branch onto the already-fetched {command.Remote.Value}/{command.Destination.Value} tracking ref. Conflicts may require manual resolution.",
            DeveloperGitRemoteAction.Push when command.PushPolicy == DeveloperGitPushPolicy.ForceWithLease =>
                $"Push {command.Source.Value} to {command.Remote.Value}/{command.Destination.Value} with a lease bound to the displayed remote-tracking commit.",
            _ => $"Push {command.Source.Value} to {command.Remote.Value}/{command.Destination.Value} only when the remote accepts a fast-forward update.",
        };
        string recovery = command.Action switch
        {
            DeveloperGitRemoteAction.Fetch => "Fetch changes only remote-tracking references; local commits and working files remain unchanged.",
            DeveloperGitRemoteAction.Push => "A remote update may require remote-side reflog or administrator recovery; Harness does not guarantee it.",
            _ => "Local reflog may retain prior commits, but Harness does not guarantee automatic recovery from integration conflicts.",
        };
        string identity = string.Join('\0', inspection.Context.WorkspaceId.Value,
            inspection.State.Fingerprint, command.Action, command.Remote.Value, command.Source.Value,
            command.Destination.Value, command.PushPolicy, inspection.LocalSha,
            inspection.RemoteTrackingSha);
        var preview = new DeveloperGitRemotePreviewView(
            new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()),
            inspection.Context, new(inspection.State.Fingerprint), command.Action, command.Remote,
            command.Source, command.Destination, inspection.LocalSha, inspection.RemoteTrackingSha,
            command.PushPolicy, inspection.Ahead, inspection.Behind, consequence,
            "Configured Git credential helpers or SSH agent; credential values are never displayed or persisted by Harness.",
            recovery);
        return new(preview, inspection, null, null);
    }

    public async ValueTask<DeveloperGitRemoteInspectionResult> ApplyRemoteAsync(
        DeveloperGitRemotePreviewView preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        DeveloperGitRemotePreviewResult current = await PreviewRemoteAsync(new(
            new(preview.Context.WorkspaceId, GoalId: null), preview.Fingerprint, preview.Action,
            preview.Remote, preview.Source, preview.Destination, preview.PushPolicy), cancellationToken);
        if (current.Preview is null || current.Error is not null) return current.Inspection;
        if (current.Preview.Id != preview.Id)
            return current.Inspection with { ErrorCode = "git_remote_preview_stale",
                Error = "The remote operation preview changed. Review it again before applying." };

        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            new(preview.Context.WorkspaceId, GoalId: null), cancellationToken);
        if (resolution.RootPath is null || resolution.Error is not null)
            return current.Inspection with { ErrorCode = resolution.ErrorCode, Error = resolution.Error };
        DeveloperGitRemoteResult result = await repository.ApplyRemoteAsync(new(
            resolution.RootPath, new(preview.Fingerprint.Value), preview.Action switch
            {
                DeveloperGitRemoteAction.Fetch => DeveloperGitRemoteOperation.Fetch,
                DeveloperGitRemoteAction.PullMerge => DeveloperGitRemoteOperation.PullMerge,
                DeveloperGitRemoteAction.PullRebase => DeveloperGitRemoteOperation.PullRebase,
                _ => DeveloperGitRemoteOperation.Push,
            }, new(preview.Remote.Value), new(preview.Source.Value), new(preview.Destination.Value),
            preview.ExpectedLocalSha, preview.ExpectedRemoteTrackingSha,
            preview.PushPolicy == DeveloperGitPushPolicy.ForceWithLease
                ? Harness.DataAccess.Inspection.DeveloperGitPushPolicy.ForceWithLease
                : Harness.DataAccess.Inspection.DeveloperGitPushPolicy.FastForwardOnly), cancellationToken);
        DeveloperGitRemoteInspectionResult mapped = MapRemote(preview.Context, result.Inspection);
        return mapped with { ErrorCode = result.ErrorCode ?? mapped.ErrorCode,
            Error = result.Error ?? mapped.Error };
    }

}
