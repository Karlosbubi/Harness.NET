using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;
using Harness.UI.Avalonia;
using AvaloniaOrientation = Avalonia.Layout.Orientation;
using DockAlignment = Dock.Model.Core.Alignment;
using DockOrientation = Dock.Model.Core.Orientation;

namespace Harness.Presentation.Avalonia;

internal sealed partial class WorkbenchDockHost
{
    internal ValueTask<bool> SaveActiveSourceDocumentAsync() => documentsHost.SaveActiveAsync();
    internal ValueTask CloseActiveSourceDocumentAsync() => documentsHost.CloseActiveAsync();

    internal ValueTask RestoreLayoutAsync() => layoutHost.RestoreAsync();

    internal ValueTask SaveLayoutAsync(CancellationToken saveCancellationToken = default) =>
        layoutHost.SaveAsync(saveCancellationToken);

    internal ValueTask ResetLayoutAsync() => layoutHost.ResetAsync();

    internal async ValueTask RefreshAsync()
    {
        Update(state());
        if (ActiveWorkspace() is { IsTrusted: true })
        {
            await filesTool.RefreshAsync();
            await solutionTool.RefreshAsync();
            await RefreshGitAsync();
        }
    }

    internal async ValueTask<bool> PrepareForShutdownAsync()
    {
        if (!await gitConflictsTool.ResolveUnsavedAsync(WorkbenchDocumentTransition.Exit)) return false;
        if (!await documentsHost.PrepareForShutdownAsync()) return false;
        await gitConflictsTool.InvalidateCodeIntelligenceAsync();
        return true;
    }

    internal async ValueTask<bool> PrepareForWorkspaceChangeAsync()
    {
        if (!await gitConflictsTool.ResolveUnsavedAsync(WorkbenchDocumentTransition.Switch)) return false;
        return await documentsHost.PrepareForWorkspaceChangeAsync();
    }

    internal void Update(AvaloniaShellState snapshot)
    {
        filesTool.Update(snapshot);
        solutionTool.Update(snapshot);
        documentsHost.Update(snapshot);
        navigator.Update(snapshot.Settings.KeybindingSettings ?? KeybindingSettingsSnapshot.Default);

        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        if (!string.Equals(workspaceId, active?.Id, StringComparison.Ordinal))
        {
            workspaceId = active?.Id;
            Dispatcher.UIThread.Post(async () =>
                await documentsHost.CloseAllAsync(WorkbenchDocumentTransition.Close));
            Dispatcher.UIThread.Post(async () => await InvalidateCodeIntelligenceAsync());
            gitChangesTool.Reset(active, sourceContextChanged: false);
            gitConflictsTool.Clear();
        }

        GoalView? selectedGoal = snapshot.Goals.SelectedGoal;
        string? nextGoalId = selectedGoal is not null && active is not null &&
                             selectedGoal.WorkspaceId == active.Id
            ? selectedGoal.Id.Value
            : null;
        if (!string.Equals(selectedGoalId, nextGoalId, StringComparison.Ordinal))
        {
            selectedGoalId = nextGoalId;
            Dispatcher.UIThread.Post(async () => await InvalidateCodeIntelligenceAsync());
            gitChangesTool.Reset(active, sourceContextChanged: true);
            if (active is { IsTrusted: true })
            {
                Dispatcher.UIThread.Post(async () => await RefreshGitAsync());
            }
        }

        runOutputToolUnit.Update(snapshot, selectedGoal?.Id);

        overviewHost.Update(active);
    }

    internal ValueTask OpenFileAsync(string relativePath) =>
        documentsHost.OpenAsync(relativePath);

    private ValueTask OpenFileAsync(string relativePath, GoalId? goalId) =>
        documentsHost.OpenAsync(relativePath, goalId);

    internal ValueTask<InboundUiActionResult> OpenInboundDocumentAsync(
        InboundUiDocumentRequest request) => documentsHost.OpenInboundAsync(request);

    /// <summary>
    /// Offers each Git-tracked file as a command that opens it. The catalog is loaded on
    /// demand so quick open reflects the same bounded, context-resolved file list the
    /// Files panel shows rather than a separate scan.
    /// </summary>
    internal async ValueTask<IReadOnlyList<PaletteCommand>> BuildFileCommandsAsync()
        => await filesTool.BuildFileCommandsAsync();

    internal async ValueTask RefreshGitAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted)
        {
            GitStatus.Text = active is null
                ? "Select a workspace first."
                : "Trust the workspace before inspecting Git.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkbenchGitInspectionResult inspected = await inspectionService.InspectGitAsync(
                WorkbenchRequest(active),
                cancellationToken);
            WorkspaceGitStateView git = inspected.Git;
            if (git.Error is not null)
            {
                GitStatus.Text = git.Error;
                return;
            }

            RenderGitState(inspected.Context, git);
            if (developerGitService is not null &&
                inspected.Context.Scope == WorkbenchWorkspaceScope.OriginalWorkspace)
            {
                DeveloperGitBranchInspectionResult branches = await developerGitService.InspectBranchesAsync(
                    WorkbenchRequest(active), cancellationToken);
                gitBranchesTool.RenderBranches(branches);
                if (branches.State is not null &&
                    !branches.State.Fingerprint.Equals(git.Fingerprint, StringComparison.Ordinal))
                    RenderGitState(branches.Context, branches.State);
                DeveloperGitTagInspectionResult tags = await developerGitService.InspectTagsAsync(
                    WorkbenchRequest(active), cancellationToken);
                gitBranchesTool.RenderTags(tags);
                DeveloperGitWorktreeInspectionResult worktrees =
                    await developerGitService.InspectWorktreesAsync(
                        WorkbenchRequest(active), cancellationToken);
                gitWorktreesTool.RenderWorktrees(worktrees);
                DeveloperGitStashInspectionResult stashes = await developerGitService.InspectStashesAsync(
                    WorkbenchRequest(active), cancellationToken);
                gitWorktreesTool.RenderStashes(stashes);
                gitRemotesTool.Render(await developerGitService.InspectRemotesAsync(
                    WorkbenchRequest(active), cancellationToken));
            }
            if (developerGitService is not null)
            {
                await gitHistoryTool.RefreshCoreAsync(active, append: false);
                if (!gitConflictsTool.IsDirty) await gitConflictsTool.RefreshCoreAsync(active);
                else gitConflictsTool.Status.Text =
                    "Merge result has unsaved edits; automatic Git refresh preserved this buffer.";
            }
        });
    }

    private void RenderGitState(WorkbenchWorkspaceContext context, WorkspaceGitStateView git) =>
        gitChangesTool.Render(context, git);

    internal ValueTask UpdateSelectedGitIndexAsync(DeveloperGitIndexAction action) =>
        gitChangesTool.UpdateSelectedIndexAsync(action);

    internal void SelectWholeGitFile() => gitChangesTool.SelectWholeFile();

    internal ValueTask ComposeAndCommitGitAsync() => gitChangesTool.ComposeAndCommitAsync();
    internal ValueTask RefreshGitBranchesAsync() => gitBranchesTool.RefreshBranchesAsync();

    internal ValueTask ApplyGitBranchAsync(DeveloperGitBranchAction action) =>
        gitBranchesTool.ApplyBranchAsync(action);

    internal ValueTask DeleteSelectedGitBranchAsync() =>
        gitBranchesTool.DeleteSelectedBranchAsync();

    internal ValueTask RefreshGitTagsAsync() => gitBranchesTool.RefreshTagsAsync();

    internal ValueTask CreateGitTagAsync() => gitBranchesTool.CreateTagAsync();

    internal ValueTask DeleteSelectedGitTagAsync() => gitBranchesTool.DeleteSelectedTagAsync();

    internal ValueTask RefreshGitWorktreesAsync() => gitWorktreesTool.RefreshWorktreesAsync();

    internal ValueTask CreateGitWorktreeAsync() => gitWorktreesTool.CreateWorktreeAsync();

    internal ValueTask OpenSelectedGitWorktreeAsync() => gitWorktreesTool.OpenSelectedWorktreeAsync();

    internal ValueTask RemoveSelectedGitWorktreeAsync() =>
        gitWorktreesTool.RemoveSelectedWorktreeAsync();

    internal ValueTask RefreshGitStashesAsync() => gitWorktreesTool.RefreshStashesAsync();

    internal ValueTask CreateGitStashAsync() => gitWorktreesTool.CreateStashAsync();

    internal ValueTask ApplySelectedGitStashAsync() => gitWorktreesTool.ApplySelectedStashAsync();

    internal ValueTask DropSelectedGitStashAsync() => gitWorktreesTool.DropSelectedStashAsync();

    internal ValueTask RefreshGitRemotesAsync() => gitRemotesTool.RefreshAsync();

    internal ValueTask SynchronizeGitRemoteAsync(DeveloperGitRemoteAction action) =>
        gitRemotesTool.SynchronizeAsync(action);

    internal ValueTask IntegrateGitRemoteAsync() => gitRemotesTool.IntegrateAsync();

    internal ValueTask RefreshGitHistoryAsync(bool append = false) =>
        gitHistoryTool.RefreshAsync(append);

    internal ValueTask ShowGitBlameAsync() => gitHistoryTool.ShowBlameAsync();

    internal ValueTask RefreshGitConflictsAsync() => gitConflictsTool.RefreshAsync();

    private bool IsActiveConflictDocument(string path, GoalId? goalId) =>
        gitConflictsTool.HasActiveDocument(path, goalId);

    internal ValueTask SaveGitConflictResultAsync() => gitConflictsTool.SaveAsync();

    internal ValueTask StageSavedGitConflictResultAsync() => gitConflictsTool.StageAsync();

    internal void UseGitConflictBase() => gitConflictsTool.UseBase();

    internal void UseGitConflictOurs() => gitConflictsTool.UseOurs();

    internal void UseGitConflictTheirs() => gitConflictsTool.UseTheirs();

    internal ValueTask PreviewAndApplyGitDestructiveAsync(DeveloperGitDestructiveAction action) =>
        gitChangesTool.PreviewAndApplyDestructiveAsync(action);

    private bool IsOriginalDocumentDirty(string path) => documentsHost.IsOriginalDirty(path);

    private bool HasDirtyOriginalDocuments() => documentsHost.HasDirtyOriginals();

    private ValueTask ReloadOriginalDocumentAsync(string path) =>
        documentsHost.ReloadOriginalAsync(path);

    internal async ValueTask OpenDiffAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted)
        {
            GitStatus.Text = active is null
                ? "Select a workspace first."
                : "Trust the workspace before inspecting Git.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkbenchGitInspectionResult inspected = await inspectionService.InspectGitAsync(
                WorkbenchRequest(active),
                cancellationToken);
            WorkspaceGitStateView git = inspected.Git;
            if (git.Error is not null)
            {
                GitStatus.Text = git.Error;
                return;
            }

            if (string.IsNullOrWhiteSpace(git.Diff))
            {
                GitStatus.Text = "The working tree has no textual diff.";
                return;
            }

            documentsHost.OpenOrReplace(
                DiffDocumentId(inspected.Context),
                $"{git.Branch} working diff",
                CreateDiffView(git.Diff));
            GitStatus.Text = $"Opened the current bounded Git diff · {inspected.Context.Description}.";
        });
    }

    internal void OpenPlan() => overviewHost.OpenPlan();

    internal void OpenEvidence() => overviewHost.OpenEvidence();

    internal void ApplyViewport(double width, double height) =>
        layoutHost.ApplyViewport(width, height);

    internal ValueTask RefreshFilesAsync() => filesTool.RefreshAsync();

    internal ValueTask SearchWorkspaceAsync() => filesTool.SearchAsync();

    internal ValueTask RefreshSolutionAsync() => solutionTool.RefreshAsync();

    internal ValueTask BuildStartupProjectAsync() =>
        solutionTool.StartDefaultBuildAsync(DeveloperExecutionOperation.Build);

    internal ValueTask RebuildStartupProjectAsync() =>
        solutionTool.StartDefaultBuildAsync(DeveloperExecutionOperation.Rebuild);

    private Control BuildWorkspaceNavigation(Control navigation)
    {
        TabItem workspaceTab = new() { Header = "Workspace", Content = navigation };
        TabItem solutionTab = new() { Header = "Solution", Content = solutionTool.Content };
        AutomationProperties.SetName(workspaceTab, "Workspace navigation tab");
        AutomationProperties.SetName(solutionTab, ".NET solution navigation tab");
        workspaceSections.Items.Add(workspaceTab);
        workspaceSections.Items.Add(solutionTab);
        workspaceSections.SelectedIndex = 0;
        AutomationProperties.SetName(workspaceSections, "Workspace and solution navigation");
        return workspaceSections;
    }
    private Control BuildSourceControlTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            Margin = new Thickness(10),
            RowSpacing = 8,
        };
        grid.Children.Add(gitChangesTool.Summary);
        Grid.SetRow(gitChangesTool.Actions, 1);
        grid.Children.Add(gitChangesTool.Actions);
        Control changePanel = gitChangesTool.Content;
        Control branchPanel = gitBranchesTool.BranchesContent;
        Control tagPanel = gitBranchesTool.TagsContent;
        Control worktreePanel = gitWorktreesTool.WorktreesContent;
        Control stashPanel = gitWorktreesTool.StashesContent;
        Control remotePanel = gitRemotesTool.Content;
        Control historyPanel = gitHistoryTool.Content;
        Control conflictPanel = gitConflictsTool.Content;

        TabItem changesTab = new() { Header = "Changes", Content = changePanel };
        TabItem branchesTab = new() { Header = "Branches", Content = branchPanel };
        TabItem tagsTab = new() { Header = "Tags", Content = tagPanel };
        TabItem worktreesTab = new() { Header = "Worktrees", Content = worktreePanel };
        TabItem stashesTab = new() { Header = "Stashes", Content = stashPanel };
        TabItem historyTab = new() { Header = "History", Content = historyPanel };
        TabItem conflictsTab = new() { Header = "Conflicts", Content = conflictPanel };
        TabItem remotesTab = new() { Header = "Remotes", Content = remotePanel };
        AutomationProperties.SetName(changesTab, "Git changes tab");
        AutomationProperties.SetName(branchesTab, "Git branches tab");
        AutomationProperties.SetName(tagsTab, "Git tags tab");
        AutomationProperties.SetName(worktreesTab, "Git worktrees tab");
        AutomationProperties.SetName(stashesTab, "Git stashes tab");
        AutomationProperties.SetName(historyTab, "Git history, file timeline, and blame tab");
        AutomationProperties.SetName(conflictsTab, "Git three-way conflict editor tab");
        AutomationProperties.SetName(remotesTab, "Git explicit remote synchronization tab");
        gitSections.Items.Add(changesTab);
        gitSections.Items.Add(branchesTab);
        gitSections.Items.Add(tagsTab);
        gitSections.Items.Add(worktreesTab);
        gitSections.Items.Add(stashesTab);
        gitSections.Items.Add(historyTab);
        gitSections.Items.Add(conflictsTab);
        gitSections.Items.Add(remotesTab);
        gitSections.SelectedIndex = 0;
        AutomationProperties.SetName(gitSections, "Git workbench sections");
        Grid.SetRow(gitSections, 2);
        grid.Children.Add(gitSections);

        Grid.SetRow(GitStatus, 3);
        grid.Children.Add(GitStatus);
        return grid;
    }

    private Control BuildContextTool(Control context)
    {
        Grid grid = new() { RowDefinitions = new("*,Auto"), RowSpacing = 8 };
        grid.Children.Add(context);
        StackPanel actions = new()
        {
            Orientation = AvaloniaOrientation.Horizontal,
            Margin = new Thickness(10),
            Spacing = 6,
        };
        Button plan = new() { Content = "Open plan" };
        AutomationProperties.SetName(plan, "Open selected goal plan document");
        plan.Click += (_, _) => OpenPlan();
        Button evidence = new() { Content = "Open evidence" };
        AutomationProperties.SetName(evidence, "Open selected goal workflow evidence document");
        evidence.Click += (_, _) => OpenEvidence();
        actions.Children.Add(plan);
        actions.Children.Add(evidence);
        Grid.SetRow(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private async ValueTask InvalidateCodeIntelligenceAsync()
    {
        await gitConflictsTool.InvalidateCodeIntelligenceAsync();
        await documentsHost.InvalidateAsync();
    }

    internal ValueTask RefreshRunOutputAsync() => runOutputToolUnit.RefreshAsync();

    internal ValueTask StopSelectedRunAsync() => runOutputToolUnit.CancelSelectedAsync();

    internal void ToggleProblemWarnings() => problemsToolUnit.ToggleWarnings();

    internal void ToggleProblemInformation() => problemsToolUnit.ToggleInformation();

    internal void ToggleProblemHidden() => problemsToolUnit.ToggleHidden();

    internal ValueTask TransformActiveDocumentAsync(
        WorkbenchCodeDocumentTransformationKind kind) => documentsHost.TransformActiveAsync(kind);

    internal ValueTask InspectActiveDocumentAsync(WorkbenchCodeInspectionKind kind) =>
        documentsHost.InspectActiveAsync(kind);

    internal ValueTask ShowActiveQuickFixesAsync() => documentsHost.ShowActiveQuickFixesAsync();

    internal ValueTask ApplyActiveCodeActionAsync(WorkbenchCodeActionCandidate candidate) =>
        documentsHost.ApplyActiveCodeActionAsync(candidate);

    internal ValueTask HandleActiveTextEnteredAsync(string? text) =>
        documentsHost.HandleTextEnteredAsync(text);

    internal ValueTask HandleActivePasteAsync(WorkbenchCodeRange range) =>
        documentsHost.HandlePasteAsync(range);

    internal bool CanTransformActiveDocument(WorkbenchCodeDocumentTransformationKind kind) =>
        documentsHost.CanTransform(kind);

    internal bool CanInvokeActiveEditorCommand(KeybindingCommand command) =>
        documentsHost.CanInvoke(command);

    internal ValueTask InvokeActiveEditorCommandAsync(KeybindingCommand command) =>
        documentsHost.InvokeActiveAsync(command);

    internal ValueTask<PendingWorkbenchRename?> PreviewActiveRenameAsync(string newName) =>
        documentsHost.PreviewRenameAsync(newName);

    internal ValueTask<RenameSymbolApplyView?> ApplyActiveRenameAsync(
        PendingWorkbenchRename pending) => documentsHost.ApplyRenameAsync(pending);

    internal void ReactivateDocumentForTest(IDockable document) =>
        documentsHost.ReactivateForTest(document);

}
