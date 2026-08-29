using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Inspection;

namespace Harness.Presentation.Avalonia.Workbench;

internal static class WorkbenchPaletteCatalog
{
    internal static IReadOnlyList<PaletteCommand> Build(
        WorkbenchDockHost host,
        KeybindingSettingsSnapshot bindings,
        string? needsTrust,
        string? needsGoal)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(bindings);

        return
        [
            Command("files.refresh", "Files", "Refresh repository files",
                KeybindingCommand.RefreshFiles,
                () => InFiles(host.RefreshFilesAsync), needsTrust),
            Command("files.search", "Files", "Search workspace text",
                KeybindingCommand.SearchWorkspace,
                () => InFiles(host.SearchWorkspaceAsync), needsTrust),
            Command("solution.refresh", "Solution", "Refresh .NET solution metadata",
                KeybindingCommand.RefreshSolution,
                () => InSolution(host.RefreshSolutionAsync), needsTrust),
            Command("solution.build", "Solution", "Build startup project",
                KeybindingCommand.BuildStartupProject,
                () => InSolution(host.BuildStartupProjectAsync), needsTrust),
            Command("solution.rebuild", "Solution", "Rebuild startup project",
                KeybindingCommand.RebuildStartupProject,
                () => InSolution(host.RebuildStartupProjectAsync), needsTrust),
            Command("tests.refresh", "Tests", "Refresh Test Explorer",
                KeybindingCommand.RefreshTestExplorer,
                () => InTests(host.RefreshTestExplorerAsync), needsTrust),
            Command("git.refresh", "Git", "Refresh Git state",
                KeybindingCommand.RefreshGit,
                () => InGit(GitWorkbenchSection.Changes, host.RefreshGitAsync), needsTrust),
            Command("git.stage", "Git changes", "Stage selected change",
                KeybindingCommand.StageGitChange,
                () => InGit(GitWorkbenchSection.Changes,
                    () => host.UpdateSelectedGitIndexAsync(DeveloperGitIndexAction.Stage)), needsTrust),
            Command("git.unstage", "Git changes", "Unstage selected change",
                KeybindingCommand.UnstageGitChange,
                () => InGit(GitWorkbenchSection.Changes,
                    () => host.UpdateSelectedGitIndexAsync(DeveloperGitIndexAction.Unstage)), needsTrust),
            Command("git.whole-file", "Git changes", "Select whole file",
                KeybindingCommand.SelectWholeGitFile,
                () => InGitAction(GitWorkbenchSection.Changes, host.SelectWholeGitFile), needsTrust),
            Command("git.discard", "Git changes", "Discard selected file…",
                KeybindingCommand.DiscardGitChange,
                () => InGit(GitWorkbenchSection.Changes,
                    () => host.PreviewAndApplyGitDestructiveAsync(
                        DeveloperGitDestructiveAction.DiscardTrackedWorktree)), needsTrust),
            Command("git.clean", "Git changes", "Delete selected untracked file…",
                KeybindingCommand.DeleteUntrackedGitFile,
                () => InGit(GitWorkbenchSection.Changes,
                    () => host.PreviewAndApplyGitDestructiveAsync(
                        DeveloperGitDestructiveAction.DeleteUntracked)), needsTrust),
            Command("git.commit", "Git changes", "Commit staged changes…",
                KeybindingCommand.CommitGitChange,
                () => InGit(GitWorkbenchSection.Changes, host.ComposeAndCommitGitAsync), needsTrust),
            Command("git.branches.refresh", "Git branches", "Refresh branches",
                KeybindingCommand.RefreshGitBranches,
                () => InGit(GitWorkbenchSection.Branches, host.RefreshGitBranchesAsync), needsTrust),
            Command("git.branches.create", "Git branches", "Create branch",
                KeybindingCommand.CreateGitBranch,
                () => InGit(GitWorkbenchSection.Branches,
                    () => host.ApplyGitBranchAsync(DeveloperGitBranchAction.Create)), needsTrust),
            Command("git.branches.switch", "Git branches", "Switch branch",
                KeybindingCommand.SwitchGitBranch,
                () => InGit(GitWorkbenchSection.Branches,
                    () => host.ApplyGitBranchAsync(DeveloperGitBranchAction.Switch)), needsTrust),
            Command("git.branches.rename", "Git branches", "Rename branch",
                KeybindingCommand.RenameGitBranch,
                () => InGit(GitWorkbenchSection.Branches,
                    () => host.ApplyGitBranchAsync(DeveloperGitBranchAction.Rename)), needsTrust),
            Command("git.branches.delete", "Git branches", "Delete branch…",
                KeybindingCommand.DeleteGitBranch,
                () => InGit(GitWorkbenchSection.Branches, host.DeleteSelectedGitBranchAsync), needsTrust),
            Command("git.tags.refresh", "Git tags", "Refresh tags",
                KeybindingCommand.RefreshGitTags,
                () => InGit(GitWorkbenchSection.Tags, host.RefreshGitTagsAsync), needsTrust),
            Command("git.tags.create", "Git tags", "Create tag",
                KeybindingCommand.CreateGitTag,
                () => InGit(GitWorkbenchSection.Tags, host.CreateGitTagAsync), needsTrust),
            Command("git.tags.delete", "Git tags", "Delete tag…",
                KeybindingCommand.DeleteGitTag,
                () => InGit(GitWorkbenchSection.Tags, host.DeleteSelectedGitTagAsync), needsTrust),
            Command("git.worktrees.refresh", "Git worktrees", "Refresh worktrees",
                KeybindingCommand.RefreshGitWorktrees,
                () => InGit(GitWorkbenchSection.Worktrees, host.RefreshGitWorktreesAsync), needsTrust),
            Command("git.worktrees.create", "Git worktrees", "Create worktree",
                KeybindingCommand.CreateGitWorktree,
                () => InGit(GitWorkbenchSection.Worktrees, host.CreateGitWorktreeAsync), needsTrust),
            Command("git.worktrees.open", "Git worktrees", "Open selected worktree…",
                KeybindingCommand.OpenGitWorktree,
                () => InGit(GitWorkbenchSection.Worktrees, host.OpenSelectedGitWorktreeAsync), needsTrust),
            Command("git.worktrees.remove", "Git worktrees", "Remove selected worktree…",
                KeybindingCommand.RemoveGitWorktree,
                () => InGit(GitWorkbenchSection.Worktrees, host.RemoveSelectedGitWorktreeAsync), needsTrust),
            Command("git.stashes.refresh", "Git stashes", "Refresh stashes",
                KeybindingCommand.RefreshGitStashes,
                () => InGit(GitWorkbenchSection.Stashes, host.RefreshGitStashesAsync), needsTrust),
            Command("git.stashes.create", "Git stashes", "Create stash",
                KeybindingCommand.CreateGitStash,
                () => InGit(GitWorkbenchSection.Stashes, host.CreateGitStashAsync), needsTrust),
            Command("git.stashes.apply", "Git stashes", "Apply selected stash",
                KeybindingCommand.ApplyGitStash,
                () => InGit(GitWorkbenchSection.Stashes, host.ApplySelectedGitStashAsync), needsTrust),
            Command("git.stashes.delete", "Git stashes", "Delete selected stash…",
                KeybindingCommand.DeleteGitStash,
                () => InGit(GitWorkbenchSection.Stashes, host.DropSelectedGitStashAsync), needsTrust),
            Command("git.remotes.refresh", "Git remotes", "Refresh remotes",
                KeybindingCommand.RefreshGitRemotes,
                () => InGit(GitWorkbenchSection.Remotes, host.RefreshGitRemotesAsync), needsTrust),
            Command("git.remotes.fetch", "Git remotes", "Fetch remote…",
                KeybindingCommand.FetchGitRemote,
                () => InGit(GitWorkbenchSection.Remotes,
                    () => host.SynchronizeGitRemoteAsync(DeveloperGitRemoteAction.Fetch)), needsTrust),
            Command("git.remotes.integrate", "Git remotes", "Integrate fetched commits…",
                KeybindingCommand.IntegrateGitRemote,
                () => InGit(GitWorkbenchSection.Remotes, host.IntegrateGitRemoteAsync), needsTrust),
            Command("git.remotes.push", "Git remotes", "Push remote…",
                KeybindingCommand.PushGitRemote,
                () => InGit(GitWorkbenchSection.Remotes,
                    () => host.SynchronizeGitRemoteAsync(DeveloperGitRemoteAction.Push)), needsTrust),
            Command("git.history.refresh", "Git history", "Refresh history",
                KeybindingCommand.RefreshGitHistory,
                () => InGit(GitWorkbenchSection.History, () => host.RefreshGitHistoryAsync()), needsTrust),
            Command("git.history.more", "Git history", "Load more history",
                KeybindingCommand.LoadMoreGitHistory,
                () => InGit(GitWorkbenchSection.History,
                    () => host.RefreshGitHistoryAsync(append: true)), needsTrust),
            Command("git.history.blame", "Git history", "Show blame for path",
                KeybindingCommand.ShowGitBlame,
                () => InGit(GitWorkbenchSection.History, host.ShowGitBlameAsync), needsTrust),
            Command("git.conflicts.refresh", "Git conflicts", "Refresh conflicts",
                KeybindingCommand.RefreshGitConflicts,
                () => InGit(GitWorkbenchSection.Conflicts, host.RefreshGitConflictsAsync), needsTrust),
            Command("git.conflicts.save", "Git conflicts", "Save conflict result",
                KeybindingCommand.SaveGitConflict,
                () => InGit(GitWorkbenchSection.Conflicts, host.SaveGitConflictResultAsync), needsTrust),
            Command("git.conflicts.stage", "Git conflicts", "Stage saved conflict result",
                KeybindingCommand.StageGitConflict,
                () => InGit(GitWorkbenchSection.Conflicts,
                    host.StageSavedGitConflictResultAsync), needsTrust),
            Command("git.conflicts.base", "Git conflicts", "Use conflict base",
                KeybindingCommand.UseGitConflictBase,
                () => InGitAction(GitWorkbenchSection.Conflicts, host.UseGitConflictBase), needsTrust),
            Command("git.conflicts.ours", "Git conflicts", "Use conflict ours",
                KeybindingCommand.UseGitConflictOurs,
                () => InGitAction(GitWorkbenchSection.Conflicts, host.UseGitConflictOurs), needsTrust),
            Command("git.conflicts.theirs", "Git conflicts", "Use conflict theirs",
                KeybindingCommand.UseGitConflictTheirs,
                () => InGitAction(GitWorkbenchSection.Conflicts, host.UseGitConflictTheirs), needsTrust),
            Command("run-output.refresh", "Run output", "Refresh run output",
                KeybindingCommand.RefreshRunOutput,
                () => InRunOutput(host.RefreshRunOutputAsync), needsTrust),
            Command("run-output.stop", "Run output", "Stop selected run",
                KeybindingCommand.StopSelectedRun,
                () => InRunOutput(host.StopSelectedRunAsync), needsTrust),
            Command("problems.warnings", "Problems", "Toggle warning diagnostics",
                KeybindingCommand.ToggleProblemWarnings,
                () => InProblems(host.ToggleProblemWarnings)),
            Command("problems.information", "Problems", "Toggle information diagnostics",
                KeybindingCommand.ToggleProblemInformation,
                () => InProblems(host.ToggleProblemInformation)),
            Command("problems.hidden", "Problems", "Toggle hidden diagnostics",
                KeybindingCommand.ToggleProblemHidden,
                () => InProblems(host.ToggleProblemHidden)),
            Command("goal.plan.open", "Goal context", "Open goal plan",
                KeybindingCommand.OpenGoalPlan,
                () => Invoke(host.OpenPlan), needsGoal),
            Command("goal.evidence.open", "Goal context", "Open goal evidence",
                KeybindingCommand.OpenGoalEvidence,
                () => Invoke(host.OpenEvidence), needsGoal),
        ];

        PaletteCommand Command(
            string id,
            string category,
            string title,
            KeybindingCommand binding,
            Func<ValueTask> invoke,
            string? unavailable = null) => new(
            id,
            category,
            title,
            invoke,
            bindings.DisplayFor(binding),
            unavailable,
            Binding: binding);

        async ValueTask InFiles(Func<ValueTask> action)
        {
            host.ShowFiles();
            await action();
        }

        async ValueTask InSolution(Func<ValueTask> action)
        {
            host.ShowSolution();
            await action();
        }

        async ValueTask InTests(Func<ValueTask> action)
        {
            host.ShowTestExplorer();
            await action();
        }

        async ValueTask InGit(GitWorkbenchSection section, Func<ValueTask> action)
        {
            host.ShowGit(section);
            await action();
        }

        ValueTask InGitAction(GitWorkbenchSection section, Action action)
        {
            host.ShowGit(section);
            action();
            return ValueTask.CompletedTask;
        }

        async ValueTask InRunOutput(Func<ValueTask> action)
        {
            host.ShowRunOutput();
            await action();
        }

        ValueTask InProblems(Action action)
        {
            host.ShowProblems();
            action();
            return ValueTask.CompletedTask;
        }

        static ValueTask Invoke(Action action)
        {
            action();
            return ValueTask.CompletedTask;
        }
    }
}
