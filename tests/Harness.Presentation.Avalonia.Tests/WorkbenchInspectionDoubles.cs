using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    private sealed class InspectionService : IWorkbenchInspectionService
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];
        internal string Diff { get; set; } = "first diff";
        internal string Status { get; set; } = "modified";
        internal bool IncludePatchUnit { get; set; }
        internal bool IsStaged { get; set; }
        internal bool IsUnstaged { get; set; }
        internal bool IsConflicted { get; set; }
        internal string IndexStatus { get; set; } = "Unaltered";
        internal string WorktreeStatus { get; set; } = "ModifiedInWorkdir";

        public ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchFileCatalogResult(
                Context(request),
                new(
                    [new("src/App.cs"), new("src/Feature.cs"), new("README.md")],
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
            WorkbenchWorkspaceRequest request,
            string query,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchTextSearchResult(
                Context(request),
                new(
                    [new("src/App.cs", 1, "namespace Example;")],
                    1,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            WorkbenchWorkspaceContext context = Context(request);
            return ValueTask.FromResult(new WorkbenchGitInspectionResult(
                context,
                new(
                    context.Branch?.Value ?? "main",
                    "abc123",
                    [new("src/App.cs", Status, IndexStatus, WorktreeStatus,
                        IsStaged, IsUnstaged, IsConflicted)],
                    Diff,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null,
                    Fingerprint: "git-fingerprint",
                    PatchUnits: IncludePatchUnit
                        ? [new("patch-unit", new("src/App.cs"), DeveloperGitIndexAction.Stage,
                            DeveloperGitPatchKind.Hunk, "@@ -1 +1 @@", 1, 1, "-old +new")]
                        : [])));
        }

        private static WorkbenchWorkspaceContext Context(WorkbenchWorkspaceRequest request) =>
            request.GoalId is null
                ? new(
                    request.WorkspaceId,
                    null,
                    new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace")
                : new(
                    request.WorkspaceId,
                    request.GoalId,
                    new("harness/goal-1"),
                    WorkbenchWorkspaceScope.ApprovedGoalWorktree,
                    "Approved goal worktree · harness/goal-1");
    }

    private sealed class DeveloperGitService : IDeveloperGitService
    {
        internal DeveloperGitIndexCommand? Command { get; private set; }
        internal DeveloperGitPatchCommand? PatchCommand { get; private set; }
        internal DeveloperGitDestructivePreviewCommand? DestructivePreviewCommand { get; private set; }
        internal DeveloperGitDestructivePreviewView? AppliedDestructivePreview { get; private set; }
        internal DeveloperGitCommitPreviewCommand? CommitPreviewCommand { get; private set; }
        internal DeveloperGitCommitPreviewView? AppliedCommitPreview { get; private set; }
        internal DeveloperGitBranchCommand? BranchCommand { get; private set; }
        internal DeveloperGitBranchDeletePreviewCommand? BranchDeleteCommand { get; private set; }
        internal DeveloperGitBranchDeletePreviewView? AppliedBranchDelete { get; private set; }
        internal DeveloperGitTagCreateCommand? TagCreateCommand { get; private set; }
        internal DeveloperGitTagDeletePreviewCommand? TagDeleteCommand { get; private set; }
        internal DeveloperGitTagDeletePreviewView? AppliedTagDelete { get; private set; }
        internal DeveloperGitWorktreeCreateCommand? WorktreeCreateCommand { get; private set; }
        internal DeveloperGitWorktreeRemovePreviewCommand? WorktreeRemoveCommand { get; private set; }
        internal DeveloperGitWorktreeRemovePreviewView? AppliedWorktreeRemove { get; private set; }
        internal DeveloperGitStashCreateCommand? StashCreateCommand { get; private set; }
        internal DeveloperGitStashApplyCommand? StashApplyCommand { get; private set; }
        internal DeveloperGitStashDropPreviewCommand? StashDropCommand { get; private set; }
        internal DeveloperGitStashDropPreviewView? AppliedStashDrop { get; private set; }
        internal DeveloperGitConflictSaveCommand? ConflictSaveCommand { get; private set; }
        internal DeveloperGitConflictStageCommand? ConflictStageCommand { get; private set; }

        public ValueTask<DeveloperGitIndexCommandResult> UpdateIndexAsync(
            DeveloperGitIndexCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                new(command.Workspace.WorkspaceId, command.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                null,
                command.Paths,
                null,
                null));
        }

        public ValueTask<DeveloperGitIndexCommandResult> ApplyPatchAsync(
            DeveloperGitPatchCommand command,
            CancellationToken cancellationToken = default)
        {
            PatchCommand = command;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                new(command.Workspace.WorkspaceId, command.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                null,
                [],
                null,
                null));
        }

        public ValueTask<DeveloperGitDestructivePreviewResult> PreviewDestructiveAsync(
            DeveloperGitDestructivePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            DestructivePreviewCommand = command;
            var context = new WorkbenchWorkspaceContext(
                command.Workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitDestructivePreviewResult(
                new(
                    new("preview-id"),
                    context,
                    command.ExpectedFingerprint,
                    command.Action,
                    command.Paths,
                    "Exact destructive preview",
                    "Exact consequence.",
                    "Git does not guarantee recovery.",
                    HasGuaranteedRecovery: false),
                null,
                null,
                null));
        }

        public ValueTask<DeveloperGitIndexCommandResult> ApplyDestructiveAsync(
            DeveloperGitDestructivePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedDestructivePreview = preview;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                preview.Context, null, preview.Paths, null, null));
        }

        public ValueTask<DeveloperGitCommitPreviewResult> PreviewCommitAsync(
            DeveloperGitCommitPreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            CommitPreviewCommand = command;
            var context = new WorkbenchWorkspaceContext(
                command.Workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitCommitPreviewResult(new(
                new("commit-preview"), context, command.ExpectedFingerprint,
                command.Action, command.HookPolicy, command.Message, "main", new string('a', 40),
                "Harness Developer", "developer@harness.local", [new("src/App.cs")], "staged diff",
                "A commit will be created.", "It remains in Git history.", false),
                null, null, null));
        }

        public ValueTask<DeveloperGitCommitCommandResult> CommitAsync(
            DeveloperGitCommitPreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedCommitPreview = preview;
            return ValueTask.FromResult(new DeveloperGitCommitCommandResult(
                preview.Context, null, new string('c', 40), null, null));
        }

        public ValueTask<DeveloperGitBranchInspectionResult> InspectBranchesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitBranchInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new(new("main"), new string('a', 40), true, true),
                 new(new("feature"), new string('b', 40), false, false)], null, null));
        }

        public ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchAsync(
            DeveloperGitBranchCommand command,
            CancellationToken cancellationToken = default)
        {
            BranchCommand = command;
            return InspectBranchesAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitBranchDeletePreviewResult> PreviewBranchDeleteAsync(
            DeveloperGitBranchDeletePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            BranchDeleteCommand = command;
            DeveloperGitBranchInspectionResult inspection = await InspectBranchesAsync(
                command.Workspace, cancellationToken);
            DeveloperGitBranchView branch = inspection.Branches.Single(item => item.Name == command.Name);
            return new(new(new("delete-preview"), inspection.Context, command.ExpectedFingerprint,
                branch, command.Force, "Delete branch.", "Recovery is not guaranteed.", false),
                inspection, null, null);
        }

        public ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchDeleteAsync(
            DeveloperGitBranchDeletePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedBranchDelete = preview;
            return InspectBranchesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitTagInspectionResult> InspectTagsAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitTagInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new(new("v1.0"), new string('a', 40), true, "Release", false)], null, null));
        }

        public ValueTask<DeveloperGitTagInspectionResult> CreateTagAsync(
            DeveloperGitTagCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            TagCreateCommand = command;
            return InspectTagsAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitTagDeletePreviewResult> PreviewTagDeleteAsync(
            DeveloperGitTagDeletePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            TagDeleteCommand = command;
            DeveloperGitTagInspectionResult inspection = await InspectTagsAsync(
                command.Workspace, cancellationToken);
            DeveloperGitTagView tag = inspection.Tags.Single(item => item.Name == command.Name);
            return new(new(new("tag-delete-preview"), inspection.Context, command.ExpectedFingerprint,
                tag, "Delete tag.", "Recovery is not guaranteed.", false), inspection, null, null);
        }

        public ValueTask<DeveloperGitTagInspectionResult> ApplyTagDeleteAsync(
            DeveloperGitTagDeletePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedTagDelete = preview;
            return InspectTagsAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitWorktreeInspectionResult> InspectWorktreesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitWorktreeInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                new("worktree-fingerprint"),
                [
                    new(new("/work/repository"), new("main"), new string('a', 40), true,
                        false, null, false, false, false, true, new("main-worktree-state")),
                    new(new("/work/feature"), new("feature"), new string('b', 40), false,
                        false, null, false, false, false, false, new("feature-worktree-state")),
                ], null, null));
        }

        public ValueTask<DeveloperGitWorktreeInspectionResult> CreateWorktreeAsync(
            DeveloperGitWorktreeCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            WorktreeCreateCommand = command;
            return InspectWorktreesAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitWorktreeRemovePreviewResult> PreviewWorktreeRemoveAsync(
            DeveloperGitWorktreeRemovePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            WorktreeRemoveCommand = command;
            DeveloperGitWorktreeInspectionResult inspection = await InspectWorktreesAsync(
                command.Workspace, cancellationToken);
            DeveloperGitWorktreeView worktree = inspection.Worktrees.Single(item => item.Path == command.Path);
            return new(new(new("worktree-remove-preview"), inspection.Context,
                command.ExpectedFingerprint, command.ExpectedWorktreeFingerprint, worktree,
                command.Force, "Remove worktree.", "The branch remains.", true),
                inspection, null, null);
        }

        public ValueTask<DeveloperGitWorktreeInspectionResult> ApplyWorktreeRemoveAsync(
            DeveloperGitWorktreeRemovePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedWorktreeRemove = preview;
            return InspectWorktreesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitStashInspectionResult> InspectStashesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitStashInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new("stash@{0}", new(new string('c', 40)), new string('a', 40),
                    DateTimeOffset.UnixEpoch, "On main: checkpoint", false)],
                null, null, null));
        }

        public ValueTask<DeveloperGitStashInspectionResult> CreateStashAsync(
            DeveloperGitStashCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            StashCreateCommand = command;
            return InspectStashesAsync(command.Workspace, cancellationToken);
        }

        public ValueTask<DeveloperGitStashInspectionResult> ApplyStashAsync(
            DeveloperGitStashApplyCommand command,
            CancellationToken cancellationToken = default)
        {
            StashApplyCommand = command;
            return InspectStashesAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitStashDropPreviewResult> PreviewStashDropAsync(
            DeveloperGitStashDropPreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            StashDropCommand = command;
            DeveloperGitStashInspectionResult inspection = await InspectStashesAsync(
                command.Workspace, cancellationToken);
            DeveloperGitStashView stash = inspection.Stashes.Single(item => item.CommitSha == command.Stash);
            return new(new(new("stash-drop-preview"), inspection.Context,
                command.ExpectedFingerprint, stash, "Drop stash.", "Recovery is not guaranteed.", false),
                inspection, null, null);
        }

        public ValueTask<DeveloperGitStashInspectionResult> ApplyStashDropAsync(
            DeveloperGitStashDropPreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedStashDrop = preview;
            return InspectStashesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitHistoryPageView> InspectHistoryAsync(
            DeveloperGitHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(request.Workspace.WorkspaceId,
                request.Workspace.GoalId, new("main"), WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace");
            return ValueTask.FromResult(new DeveloperGitHistoryPageView(context, null, request.Path,
                [new(new(new string('a', 40)), [], "Developer", DateTimeOffset.UnixEpoch,
                    "Initial", ["main"])], null, null, null));
        }

        public ValueTask<DeveloperGitCommitDetailResult> InspectCommitAsync(
            WorkbenchWorkspaceRequest workspace,
            DeveloperGitCommitSha commit,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, workspace.GoalId,
                new("main"), WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitCommitDetailResult(context, null,
                new(commit, [], "Developer", "developer@harness.local", DateTimeOffset.UnixEpoch,
                    "Developer", "developer@harness.local", DateTimeOffset.UnixEpoch,
                    "Initial", false, ["main"], [new(null, [new("README.md")], "patch", false)]),
                null, null));
        }

        public ValueTask<DeveloperGitBlamePageView> InspectBlameAsync(
            DeveloperGitBlameRequest request,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(request.Workspace.WorkspaceId,
                request.Workspace.GoalId, new("main"), WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace");
            return ValueTask.FromResult(new DeveloperGitBlamePageView(context, null, request.Path,
                [new(1, new(new string('a', 40)), "Developer", DateTimeOffset.UnixEpoch,
                    request.Path, 1, "line")], null, null, null));
        }

        public ValueTask<DeveloperGitConflictInspectionResult> InspectConflictsAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, workspace.GoalId,
                new("main"), WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitConflictInspectionResult(context,
                ConflictState(),
                [new(new("first.cs"), new(new string('a', 40)), new(new string('b', 40)),
                    new(new string('c', 40)), false)],
                false, null, null));
        }

        public ValueTask<DeveloperGitConflictDocumentResult> InspectConflictAsync(
            WorkbenchWorkspaceRequest workspace,
            DeveloperGitPath path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ConflictDocument(workspace, path,
                "<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n"));

        public ValueTask<DeveloperGitConflictDocumentResult> SaveConflictResultAsync(
            DeveloperGitConflictSaveCommand command,
            CancellationToken cancellationToken = default)
        {
            ConflictSaveCommand = command;
            return ValueTask.FromResult(ConflictDocument(
                command.Workspace, command.Path, command.Result));
        }

        public ValueTask<DeveloperGitIndexCommandResult> StageConflictResultAsync(
            DeveloperGitConflictStageCommand command,
            CancellationToken cancellationToken = default)
        {
            ConflictStageCommand = command;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                new(command.Workspace.WorkspaceId, command.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                ConflictState(), [command.Path], null, null));
        }

        public ValueTask<DeveloperGitRemoteInspectionResult> InspectRemotesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitRemoteInspectionResult(context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new(new("origin"), "https://example.test/repository.git", [], [])],
                new("main"), new("origin"), new("main"), new string('a', 40),
                new string('a', 40), 0, 0, null, null));
        }

        public async ValueTask<DeveloperGitRemotePreviewResult> PreviewRemoteAsync(
            DeveloperGitRemotePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            DeveloperGitRemoteInspectionResult inspection = await InspectRemotesAsync(
                command.Workspace, cancellationToken);
            return new(new(new("remote-preview"), inspection.Context, command.ExpectedFingerprint,
                command.Action, command.Remote, command.Source, command.Destination,
                inspection.LocalSha, inspection.RemoteTrackingSha, command.PushPolicy,
                inspection.Ahead, inspection.Behind, "Synchronize refs.",
                "Configured Git helper.", "Recovery is not guaranteed."), inspection, null, null);
        }

        public ValueTask<DeveloperGitRemoteInspectionResult> ApplyRemoteAsync(
            DeveloperGitRemotePreviewView preview,
            CancellationToken cancellationToken = default) =>
            InspectRemotesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);

        private static DeveloperGitConflictDocumentResult ConflictDocument(
            WorkbenchWorkspaceRequest workspace,
            DeveloperGitPath path,
            string result)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, workspace.GoalId,
                new("main"), WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return new(context, ConflictState(), new(path,
                new(path, new(new string('a', 40)), "base", false, false, false),
                new(path, new(new string('b', 40)), "ours", false, false, false),
                new(path, new(new string('c', 40)), "theirs", false, false, false),
                result, new(new string('d', 64)), false,
                result.Contains("<<<<<<<", StringComparison.Ordinal)
                    ? [new(1, 3, 5, "ours", "theirs", true)] : []), null, null);
        }

        private static WorkspaceGitStateView ConflictState() => new(
            "main", new string('a', 40),
            [new("first.cs", "Conflicted", "Conflicted", "Conflicted", false, true, true)],
            string.Empty, false, null, null, "conflict-state");
    }

    private sealed class RunOutputService : IRunOutputService
    {
        internal RunOutputSnapshot Result { get; set; } = new([], false, null, null);
        internal List<GoalId> Requests { get; } = [];

        public ValueTask<RunOutputSnapshot> ListAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(goalId);
            return ValueTask.FromResult(Result);
        }
    }

}
