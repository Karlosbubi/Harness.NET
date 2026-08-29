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

internal sealed class WorkbenchDockHost
{
    private readonly IWorkbenchInspectionService inspectionService;
    private readonly IDeveloperGitService? developerGitService;
    private readonly Func<AvaloniaShellState> state;
    private readonly Func<string, Task> manageWorkspaceAt;
    private readonly Func<Task> refreshWorkspaceContext;
    private readonly IDeveloperProjectExecutionService? developerExecutionService;
    private readonly CancellationToken cancellationToken;
    private readonly Factory factory = new();
    private readonly Dictionary<string, Control> durableContexts = new(StringComparer.Ordinal);
    private readonly FilesTool filesTool;
    private readonly GitChangesTool gitChangesTool;
    private readonly GitBranchesTool gitBranchesTool;
    private readonly GitWorktreesTool gitWorktreesTool;
    private readonly GitRemotesTool gitRemotesTool;
    private readonly GitHistoryTool gitHistoryTool;
    private readonly GitConflictsTool gitConflictsTool;
    private readonly RunOutputTool runOutputToolUnit;
    private readonly DocumentsHost documentsHost;
    private readonly ProblemsTool problemsToolUnit;
    private readonly WorkbenchOverview overviewHost;
    private readonly WorkbenchLayoutHost layoutHost;
    private readonly WorkbenchNavigator navigator;
    private TextBlock GitStatus => gitChangesTool.Status;
    private string GitFingerprint => gitChangesTool.Fingerprint;
    private WorkbenchWorkspaceContext? CurrentGitContext => gitChangesTool.CurrentContext;
    private string? workspaceId;
    private string? selectedGoalId;
    private bool busy;

    internal WorkbenchDockHost(
        IRunOutputService runOutputService,
        IWorkbenchInspectionService inspectionService,
        IWorkbenchDocumentService documentService,
        IWorkbenchCodeIntelligenceService codeIntelligenceService,
        IWorkbenchLayoutService layoutService,
        IWorkbenchDocumentPrompt documentPrompt,
        Func<AvaloniaShellState> state,
        Control navigation,
        Control conversation,
        Control goalContext,
        CancellationToken cancellationToken,
        Func<bool, Task>? manageWorkspace = null,
        IWorkspaceMutationService? mutationService = null,
        Func<Task>? manageProjectSecrets = null,
        IDeveloperProjectExecutionService? developerExecutionService = null,
        IDeveloperGitService? developerGitService = null,
        Func<Task>? refreshWorkspaceContext = null,
        Func<string, Task>? manageWorkspaceAt = null)
    {
        this.inspectionService = inspectionService;
        this.state = state;
        this.manageWorkspaceAt = manageWorkspaceAt ?? (_ => Task.CompletedTask);
        this.developerExecutionService = developerExecutionService;
        this.developerGitService = developerGitService;
        this.refreshWorkspaceContext = refreshWorkspaceContext ?? (() => Task.CompletedTask);
        this.cancellationToken = cancellationToken;
        factory.HideToolsOnClose = true;
        WorkbenchToolContext toolContext = new(
            inspectionService,
            state,
            () => busy,
            RunAsync,
            OpenFileAsync,
            cancellationToken)
        {
            DeveloperGitService = developerGitService,
            DocumentPrompt = documentPrompt,
            OwnerWindow = OwnerWindow,
            RefreshGitAsync = RefreshGitAsync,
            OpenGitDiffAsync = OpenDiffAsync,
            IsOriginalDocumentDirty = IsOriginalDocumentDirty,
            HasDirtyOriginalDocuments = HasDirtyOriginalDocuments,
            ReloadOriginalDocumentAsync = ReloadOriginalDocumentAsync,
        };
        filesTool = new(toolContext);
        gitChangesTool = new(toolContext);
        gitBranchesTool = new(
            toolContext,
            gitChangesTool.Render,
            gitChangesTool.ReportStatus,
            PrepareForWorkspaceChangeAsync,
            async () => await this.refreshWorkspaceContext());
        gitWorktreesTool = new(
            toolContext,
            gitChangesTool.Render,
            gitChangesTool.ReportStatus,
            async path => await this.manageWorkspaceAt(path));
        gitRemotesTool = new(
            toolContext,
            gitChangesTool.Render,
            gitChangesTool.ReportStatus,
            PrepareForWorkspaceChangeAsync,
            async () => await this.refreshWorkspaceContext());
        gitHistoryTool = new(toolContext, gitChangesTool.ReportStatus);
        runOutputToolUnit = new(toolContext, runOutputService, developerExecutionService);
        documentsHost = new(
            documentService,
            codeIntelligenceService,
            mutationService,
            documentPrompt,
            developerExecutionService,
            state,
            () => busy,
            RunAsync,
            filesTool.ReportStatus,
            () => filesTool.StatusText,
            IsActiveConflictDocument,
            OwnerWindow,
            InvalidateCodeIntelligenceAsync,
            ShowRunOutput,
            RefreshRunOutputAsync,
            factory,
            cancellationToken);
        problemsToolUnit = documentsHost.Problems;
        gitConflictsTool = new(
            toolContext,
            codeIntelligenceService,
            gitChangesTool.Render,
            documentsHost.HasOpen);
        overviewHost = new(
            state,
            documentsHost,
            manageWorkspace ?? (_ => Task.CompletedTask),
            manageProjectSecrets ?? (() => Task.CompletedTask));

        Control files = filesTool.Content;
        Control sourceControl = BuildSourceControlTool();
        Control runOutput = runOutputToolUnit.Content;
        Control problemsContent = problemsToolUnit.Content;
        Control context = BuildContextTool(goalContext);
        Control overviewContent = overviewHost.Content;
        durableContexts.Add(WorkbenchDockIds.NavigationTool, navigation);
        durableContexts.Add(WorkbenchDockIds.FilesTool, files);
        durableContexts.Add(WorkbenchDockIds.ContextTool, context);
        durableContexts.Add(WorkbenchDockIds.GitTool, sourceControl);
        durableContexts.Add(WorkbenchDockIds.ConversationTool, conversation);
        durableContexts.Add(WorkbenchDockIds.RunOutputTool, runOutput);
        durableContexts.Add(WorkbenchDockIds.ProblemsTool, problemsContent);
        durableContexts.Add(WorkbenchDockIds.OverviewDocument, overviewContent);

        factory
            .Tool(out ITool? navigationTool, item => item
                .WithId(WorkbenchDockIds.NavigationTool)
                .WithTitle("Workspace")
                .WithCanClose(true)
                .WithContext(navigation))
            .Tool(out ITool? filesDockTool, item => item
                .WithId(WorkbenchDockIds.FilesTool)
                .WithTitle("Files")
                .WithCanClose(true)
                .WithContext(files))
            .Tool(out ITool? contextTool, item => item
                .WithId(WorkbenchDockIds.ContextTool)
                .WithTitle("Goal context")
                .WithCanClose(true)
                .WithContext(context))
            .Tool(out ITool? gitTool, item => item
                .WithId(WorkbenchDockIds.GitTool)
                .WithTitle("Git")
                .WithCanClose(true)
                .WithContext(sourceControl))
            .Tool(out ITool? conversationTool, item => item
                .WithId(WorkbenchDockIds.ConversationTool)
                .WithTitle("Conversation")
                .WithCanClose(true)
                .WithContext(conversation))
            .Tool(out ITool? runOutputTool, item => item
                .WithId(WorkbenchDockIds.RunOutputTool)
                .WithTitle("Run output")
                .WithCanClose(true)
                .WithContext(runOutput))
            .Tool(out ITool? problemsTool, item => item
                .WithId(WorkbenchDockIds.ProblemsTool)
                .WithTitle("Problems")
                .WithCanClose(true)
                .WithContext(problemsContent))
            .Document(out IDocument? overview, item => item
                .WithId(WorkbenchDockIds.OverviewDocument)
                .WithTitle("Workspace overview")
                .WithCanClose(false)
                .WithCanFloat(false)
                .WithContext(overviewContent))
            .DocumentDock(out IDocumentDock? documentDock, dock => dock
                .WithId(WorkbenchDockIds.Documents)
                .WithTitle("Editor")
                .WithIsCollapsable(false)
                .WithCanCloseLastDockable(false)
                .WithCanCreateDocument(false))
            .ToolDock(out IToolDock left, DockAlignment.Left, dock => dock
                .WithId(WorkbenchDockIds.Left)
                .WithIsExpanded(true)
                .WithAutoHide(false))
            .ToolDock(out IToolDock right, DockAlignment.Right, dock => dock
                .WithId(WorkbenchDockIds.Right)
                .WithIsExpanded(true)
                .WithAutoHide(false))
            .ToolDock(out IToolDock bottom, DockAlignment.Bottom, dock => dock
                .WithId(WorkbenchDockIds.Bottom)
                .WithIsExpanded(true)
                .WithAutoHide(false))
            .ProportionalDockSplitter(out IProportionalDockSplitter leftSplitter)
            .ProportionalDockSplitter(out IProportionalDockSplitter rightSplitter)
            .ProportionalDockSplitter(out IProportionalDockSplitter bottomSplitter)
            .ProportionalDock(out IProportionalDock center, DockOrientation.Vertical, dock => dock
                .WithId(WorkbenchDockIds.Center)
                .Add(documentDock!, bottomSplitter!, bottom!))
            .ProportionalDock(out IProportionalDock workbench, DockOrientation.Horizontal, dock => dock
                .WithId(WorkbenchDockIds.Workbench)
                .Add(left!, leftSplitter!, center!, rightSplitter!, right!))
            .RootDock(out IRootDock rootDock, dock => dock
                .WithId(WorkbenchDockIds.Root)
                .Add(workbench!)
                .WithDefaultDockable(workbench)
                .WithActiveDockable(workbench));

        IDocumentDock documents = documentDock ?? throw new InvalidOperationException(
            "Dock did not create the document region.");
        IToolDock leftTools = left;
        IToolDock rightTools = right;
        IToolDock bottomTools = bottom;
        IDockable overviewDocument = overview ?? throw new InvalidOperationException(
            "Dock did not create the overview document.");
        left!.WithProportion(0.19);
        right!.WithProportion(0.22);
        bottom!.WithProportion(0.45);
        IRootDock root = rootDock ?? throw new InvalidOperationException(
            "Dock did not create the workbench root.");
        left.VisibleDockables = factory.CreateList<IDockable>(navigationTool!, filesDockTool!);
        left.ActiveDockable = navigationTool;
        right.VisibleDockables = factory.CreateList<IDockable>(contextTool!, gitTool!);
        right.ActiveDockable = contextTool;
        bottom.VisibleDockables = factory.CreateList<IDockable>(
            conversationTool!,
            problemsTool!,
            runOutputTool!);
        bottom.ActiveDockable = conversationTool;
        documents.VisibleDockables = factory.CreateList<IDockable>(overviewDocument);
        documents.ActiveDockable = overviewDocument;
        documentsHost.Attach(documents, overviewDocument);
        WorkbenchDockContent.Attach(navigationTool!, navigation);
        WorkbenchDockContent.Attach(filesDockTool!, files);
        WorkbenchDockContent.Attach(contextTool!, context);
        WorkbenchDockContent.Attach(gitTool!, sourceControl);
        WorkbenchDockContent.Attach(conversationTool!, conversation);
        WorkbenchDockContent.Attach(runOutputTool!, runOutput);
        WorkbenchDockContent.Attach(problemsTool!, problemsContent);
        WorkbenchDockContent.Attach(overviewDocument, overviewContent);
        WorkbenchLayoutHost.EnsureDefaultTools(left, right, bottom, "before Dock initialization");
        factory.InitLayout(root);
        WorkbenchLayoutHost.EnsureDefaultTools(left, right, bottom, "after Dock initialization");
        left.IsExpanded = true;
        right.IsExpanded = true;
        bottom.IsExpanded = true;
        factory.WindowAdded += (_, args) =>
        {
            if (args.Window is { } window)
            {
                window.OwnerMode = DockWindowOwnerMode.DockableWindow;
                window.ShowInTaskbar = false;
            }
        };
        Control = new DockControl
        {
            Factory = factory,
            Layout = root,
            Focusable = true,
        };
        AutomationProperties.SetName(Control, "Docked workspace workbench");
        layoutHost = new(
            layoutService,
            factory,
            root,
            documents,
            overviewDocument,
            leftTools,
            rightTools,
            bottomTools,
            durableContexts,
            documentsHost.CloseAllAsync,
            documentsHost.ReplaceDock,
            Control,
            cancellationToken);
        navigator = new(factory, layoutHost, Control);
        Control.SizeChanged += (_, _) =>
            layoutHost.ApplyViewport(Control.Bounds.Width, Control.Bounds.Height);
        LayoutActions = layoutHost.Actions;
        DocumentActions = documentsHost.BuildActions(
            layoutHost.Status,
            navigator.RecordFocusRequest,
            navigator.FocusContext);
    }

    internal DockControl Control { get; }
    internal Control LayoutActions { get; }
    internal Control DocumentActions { get; }
    internal ComboBox DocumentSwitcher => documentsHost.Switcher;
    internal Button OverviewAction => overviewHost.Action;
    internal IDocumentDock Documents => layoutHost.Documents;
    internal IRootDock Root => layoutHost.Root;
    internal IFactory Factory => factory;
    internal string? LayoutStatusText => layoutHost.Status.Text;
    internal bool IsCompactViewport => layoutHost.IsCompactViewport;
    internal Control? LastRequestedFocusTarget => navigator.LastRequestedFocusTarget;
    internal int SourceDocumentCount => documentsHost.SourceCount;
    internal int VirtualDocumentCount => documentsHost.VirtualCount;
    internal TreeView FileTree => filesTool.Tree;
    internal TextBox FileFilter => filesTool.Filter;
    internal TextEditor? ActiveSourceEditor => documentsHost.ActiveSourceEditor;
    internal TextEditor? ActiveVirtualEditor => documentsHost.ActiveVirtualEditor;
    internal ListBox Problems => problemsToolUnit.List;
    internal string? ProblemsStatusText => problemsToolUnit.Status.Text;
    internal bool ActiveSourceDocumentIsDirty => documentsHost.ActiveSourceIsDirty;
    internal IReadOnlyList<InboundOpenDocumentView> InboundOpenDocuments =>
        documentsHost.InboundOpenDocuments;
    internal int ActiveCompletionItemCount => documentsHost.ActiveCompletionItemCount;
    internal CompletionWindow? ActiveCompletionWindow => documentsHost.ActiveCompletionWindow;
    internal bool ActiveQuickInfoIsOpen => documentsHost.ActiveQuickInfoIsOpen;
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

    internal ValueTask RefreshGitHistoryAsync(bool append = false) =>
        gitHistoryTool.RefreshAsync(append);

    internal ValueTask RefreshGitConflictsAsync() => gitConflictsTool.RefreshAsync();

    private bool IsActiveConflictDocument(string path, GoalId? goalId) =>
        gitConflictsTool.HasActiveDocument(path, goalId);

    internal ValueTask SaveGitConflictResultAsync() => gitConflictsTool.SaveAsync();

    internal ValueTask StageSavedGitConflictResultAsync() => gitConflictsTool.StageAsync();

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
        TabControl tabs = new();
        tabs.Items.Add(changesTab);
        tabs.Items.Add(branchesTab);
        tabs.Items.Add(tagsTab);
        tabs.Items.Add(worktreesTab);
        tabs.Items.Add(stashesTab);
        tabs.Items.Add(historyTab);
        tabs.Items.Add(conflictsTab);
        tabs.Items.Add(remotesTab);
        tabs.SelectedIndex = 0;
        AutomationProperties.SetName(tabs, "Git workbench sections");
        Grid.SetRow(tabs, 2);
        grid.Children.Add(tabs);

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

    private Window? OwnerWindow() => TopLevel.GetTopLevel(Control) as Window;

    private static Control CreateDiffView(string diff)
    {
        Control view = DiffContentView.Create(diff);
        AutomationProperties.SetName(view, "Git working-tree diff");
        return view;
    }

    private WorkspaceView? ActiveWorkspace() =>
        state().Workspaces.Registered.FirstOrDefault(item => item.IsActive);

    private WorkbenchWorkspaceRequest WorkbenchRequest(WorkspaceView workspace)
    {
        GoalView? goal = state().Goals.SelectedGoal;
        return new(
            new(workspace.Id),
            goal?.WorkspaceId == workspace.Id ? goal.Id : null);
    }

    private static string DiffDocumentId(WorkbenchWorkspaceContext context) =>
        $"{WorkbenchDockIds.DiffDocument}.{context.WorkspaceId.Value}." +
        (context.GoalId?.Value ?? "original");

    internal bool ShowFiles() => navigator.ShowFiles();

    internal bool ShowConversation() => navigator.ShowConversation();

    internal bool ShowGit() => navigator.ShowGit();

    internal string GitStatusText => GitStatus.Text ?? string.Empty;

    internal string GitSummaryText => gitChangesTool.Summary.Text ?? string.Empty;

    internal bool ShowRunOutput() => navigator.ShowRunOutput();

    internal bool ShowProblems() => navigator.ShowProblems();

    internal bool FocusNextRegion() => navigator.FocusNextRegion();

    private async ValueTask RunAsync(Func<ValueTask> operation)
    {
        busy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            filesTool.ReportStatus("Workspace operation cancelled.");
            GitStatus.Text = "Workspace operation cancelled.";
        }
        catch (Exception exception)
        {
            filesTool.ReportStatus(exception.Message);
            GitStatus.Text = exception.Message;
        }
        finally
        {
            busy = false;
        }
    }

}
