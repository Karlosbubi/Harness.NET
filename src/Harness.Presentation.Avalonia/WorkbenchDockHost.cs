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
    private readonly Func<bool, Task> manageWorkspace;
    private readonly Func<string, Task> manageWorkspaceAt;
    private readonly Func<Task> manageProjectSecrets;
    private readonly Func<Task> refreshWorkspaceContext;
    private readonly IDeveloperProjectExecutionService? developerExecutionService;
    private readonly CancellationToken cancellationToken;
    private readonly Factory factory = new();
    private readonly Dictionary<string, Control> durableContexts = new(StringComparer.Ordinal);
    private readonly TextBlock overviewHeading = new()
    {
        FontSize = 22,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock overviewDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button overviewAction = new() { Content = "Open workspace" };
    private readonly Button overviewSecretsAction = new()
    {
        Content = "Project User Secrets",
        IsVisible = false,
    };
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
    private readonly WorkbenchLayoutHost layoutHost;
    private TextBlock GitStatus => gitChangesTool.Status;
    private string GitFingerprint => gitChangesTool.Fingerprint;
    private WorkbenchWorkspaceContext? CurrentGitContext => gitChangesTool.CurrentContext;
    private string? workspaceId;
    private string? selectedGoalId;
    private bool busy;
    private int focusRegionIndex = -1;
    private KeybindingSettingsSnapshot keybindingSettings = KeybindingSettingsSnapshot.Default;
    private static readonly KeybindingCommand[] WorkbenchKeyCommands =
    [
        KeybindingCommand.ShowFiles,
        KeybindingCommand.ShowGit,
        KeybindingCommand.ShowRunOutput,
        KeybindingCommand.ShowProblems,
        KeybindingCommand.FocusNextRegion,
    ];

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
        this.manageWorkspace = manageWorkspace ?? (_ => Task.CompletedTask);
        this.manageWorkspaceAt = manageWorkspaceAt ?? (_ => Task.CompletedTask);
        this.manageProjectSecrets = manageProjectSecrets ?? (() => Task.CompletedTask);
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

        Control files = filesTool.Content;
        Control sourceControl = BuildSourceControlTool();
        Control runOutput = runOutputToolUnit.Content;
        Control problemsContent = problemsToolUnit.Content;
        Control context = BuildContextTool(goalContext);
        Control overviewContent = BuildOverviewDocument();
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
        Control.KeyDown += OnWorkbenchKeyDown;
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
        Control.SizeChanged += (_, _) =>
            layoutHost.ApplyViewport(Control.Bounds.Width, Control.Bounds.Height);
        Control.LayoutUpdated += (_, _) => ApplyDockAutomationNames();
        LayoutActions = layoutHost.Actions;
        DocumentActions = documentsHost.BuildActions(
            layoutHost.Status,
            control => LastRequestedFocusTarget = control,
            FocusContext);
    }

    internal DockControl Control { get; }
    internal Control LayoutActions { get; }
    internal Control DocumentActions { get; }
    internal ComboBox DocumentSwitcher => documentsHost.Switcher;
    internal Button OverviewAction => overviewAction;
    internal IDocumentDock Documents => layoutHost.Documents;
    internal IRootDock Root => layoutHost.Root;
    internal IFactory Factory => factory;
    internal string? LayoutStatusText => layoutHost.Status.Text;
    internal bool IsCompactViewport => layoutHost.IsCompactViewport;
    internal Control? LastRequestedFocusTarget { get; private set; }
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
        keybindingSettings = snapshot.Settings.KeybindingSettings ?? KeybindingSettingsSnapshot.Default;

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

        if (active is null)
        {
            overviewHeading.Text = "Open a repository to get started";
            overviewDetails.Text = "Choose a Git-backed .NET repository. Harness.NET will discover its solutions and projects before asking you to trust it.";
            overviewAction.Content = "Open workspace";
            overviewAction.Classes.Remove("command");
            overviewAction.Classes.Add("primary");
            overviewSecretsAction.IsVisible = false;
            return;
        }

        overviewHeading.Text = active.Name;
        overviewDetails.Text = $"{active.RootPath}\n\nBranch: {active.Branch}\n" +
                               $"Trust: {(active.IsTrusted ? "Trusted" : "Not trusted")}\n" +
                               $"Working tree: {(active.IsDirty ? "Has changes" : "Clean")}\n\n" +
                               (active.IsTrusted
                                   ? "Use Files or Git to open source and diff documents in this editor."
                                   : "Trust this workspace before reading repository content.");
        overviewAction.Content = "Workspace settings";
        overviewAction.Classes.Remove("primary");
        overviewAction.Classes.Add("command");
        overviewSecretsAction.IsVisible = active.IsTrusted;
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

    internal void OpenPlan()
    {
        if (state().Goals.CurrentPlan is not { } plan)
        {
            overviewDetails.Text = "The selected goal has no current plan to open.";
            documentsHost.ActivateOverview();
            return;
        }

        documentsHost.OpenOrReplace(
            WorkbenchDockIds.PlanDocument,
            $"Plan · revision {plan.Revision.Value}",
            new ScrollViewer
            {
                Content = MarkdownContentView.Create(plan.Content, _ => null),
                Padding = new Thickness(18),
            });
    }

    internal void OpenEvidence()
    {
        if (state().Goals.Workflow?.Evidence is not { Count: > 0 } items)
        {
            overviewDetails.Text = "The selected goal has no durable workflow evidence to open.";
            documentsHost.ActivateOverview();
            return;
        }

        StackPanel content = new() { Spacing = 14 };
        foreach (var item in items)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{item.Sequence}. {item.Title.Value}",
                FontWeight = FontWeight.SemiBold,
            });
            content.Children.Add(MarkdownContentView.Create(item.Content.Value, _ => null));
        }

        documentsHost.OpenOrReplace(
            WorkbenchDockIds.EvidenceDocument,
            "Workflow evidence",
            new ScrollViewer { Content = content, Padding = new Thickness(18) });
    }

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

    private Control BuildOverviewDocument()
    {
        overviewAction.Classes.Add("primary");
        overviewAction.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(overviewAction, "Open or manage workspace");
        overviewAction.Click += async (_, _) => await manageWorkspace(ActiveWorkspace() is null);
        overviewSecretsAction.Classes.Add("command");
        overviewSecretsAction.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(overviewSecretsAction, "Manage project User Secrets");
        overviewSecretsAction.Click += async (_, _) => await manageProjectSecrets();
        StackPanel actions = new()
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children = { overviewAction, overviewSecretsAction },
        };
        return new Grid
        {
            Children =
            {
                new Border
                {
                    MaxWidth = 720,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Classes = { "card" },
                    Child = new StackPanel
                    {
                        Spacing = 14,
                        Children =
                        {
                            new TextBlock { Text = "HARNESS.NET WORKSPACE", Classes = { "eyebrow" } },
                            overviewHeading,
                            overviewDetails,
                            actions,
                        },
                    },
                },
            },
        };
    }

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

    private static Control CreateEditor(string content, string path, bool showLineNumbers)
    {
        TextEditor editor = CodeEditorView.Create(
            content, isReadOnly: true, wordWrap: false, showLineNumbers: showLineNumbers, path: path);
        AutomationProperties.SetName(editor, $"Read-only editor for {path}");
        return editor;
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

    private void OnWorkbenchKeyDown(object? sender, KeyEventArgs args)
    {
        KeybindingCommand? command = KeybindingInput.Match(
            args, keybindingSettings, WorkbenchKeyCommands);
        if (command is null) return;
        args.Handled = command switch
        {
            KeybindingCommand.ShowFiles => ShowFiles(),
            KeybindingCommand.ShowGit => ShowGit(),
            KeybindingCommand.ShowRunOutput => ShowRunOutput(),
            KeybindingCommand.ShowProblems => ShowProblems(),
            KeybindingCommand.FocusNextRegion => FocusNextRegion(),
            _ => false,
        };
    }

    /// <summary>Activates the Files panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowFiles() => ActivateTool(WorkbenchDockIds.FilesTool);

    /// <summary>Restores and activates the primary Conversation panel.</summary>
    internal bool ShowConversation() => ActivateTool(WorkbenchDockIds.ConversationTool);

    /// <summary>Activates the Git panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowGit() => ActivateTool(WorkbenchDockIds.GitTool);

    internal string GitStatusText => GitStatus.Text ?? string.Empty;

    internal string GitSummaryText => gitChangesTool.Summary.Text ?? string.Empty;

    /// <summary>Activates the Run output panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowRunOutput() => ActivateTool(WorkbenchDockIds.RunOutputTool);

    /// <summary>Activates the Problems panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowProblems() => ActivateTool(WorkbenchDockIds.ProblemsTool);

    private bool ActivateTool(string id)
    {
        IDockable? tool = layoutHost.Find(id);
        bool visibleInOwner = tool?.Owner is IDock visibleOwner &&
                              visibleOwner.VisibleDockables?.Contains(tool) is true;
        if (!visibleInOwner && factory.RestoreDockable(id) is { } restored)
        {
            tool = restored;
        }

        if (tool is null)
        {
            return false;
        }

        visibleInOwner = tool.Owner is IDock restoredOwner &&
                         restoredOwner.VisibleDockables?.Contains(tool) is true;
        if (!visibleInOwner && layoutHost.DefaultToolDock(id) is { } defaultOwner)
        {
            layoutHost.RemoveFromSpecialCollections(tool);
            factory.AddDockable(defaultOwner, tool);
        }

        if (tool.Owner is IToolDock owner)
        {
            layoutHost.RestoreAdaptiveProportion(owner);
            owner.IsExpanded = true;
        }

        factory.SetActiveDockable(tool);
        FocusContext(tool);
        return true;
    }

    private void ApplyDockAutomationNames()
    {
        foreach (DocumentTabStripItem tab in Control.GetVisualDescendants()
                     .OfType<DocumentTabStripItem>())
        {
            if (tab.DataContext is IDockable { Title: { Length: > 0 } title })
            {
                AutomationProperties.SetAccessibilityView(tab, AccessibilityView.Content);
                SetAutomationName(tab, title);
            }
        }

        foreach (ToolChromeControl chrome in Control.GetVisualDescendants()
                     .OfType<ToolChromeControl>())
        {
            if (chrome.DataContext is IToolDock dock)
            {
                SetAutomationName(chrome, $"{DockTitle(dock)} panel controls");
            }
        }

        foreach (ToolControl toolControl in Control.GetVisualDescendants().OfType<ToolControl>())
        {
            if (toolControl.DataContext is IToolDock dock)
            {
                SetAutomationName(toolControl, $"{DockTitle(dock)} panel");
            }
        }

        foreach (ItemsControl itemsControl in Control.GetVisualDescendants()
                     .OfType<ItemsControl>()
                     .Where(item => item.DataContext is IProportionalDock))
        {
            SetAutomationName(itemsControl, "Workbench panel layout");
        }

        foreach (Button button in Control.GetVisualDescendants().OfType<Button>())
        {
            string? name = button.Name switch
            {
                "PART_MenuButton" => $"Panel actions for {DockTitle(button)}",
                "PART_PinButton" => $"Auto-hide or dock {DockTitle(button)}",
                "PART_MaximizeRestoreButton" => $"Maximize or restore {DockTitle(button)}",
                "PART_CloseButton" => $"Close {DockTitle(button)}",
                _ => null,
            };
            if (name is not null)
            {
                SetAutomationName(button, name);
            }
        }

        foreach (Control splitter in Control.GetVisualDescendants()
                     .OfType<Control>()
                     .Where(item => item.GetType().Name == "ProportionalStackPanelSplitter"))
        {
            SetAutomationName(splitter, "Resize adjacent workbench panels");
        }
    }

    private static string DockTitle(Control control)
    {
        IDockable? dockable = control.GetVisualAncestors()
            .OfType<Control>()
            .Select(item => item.DataContext)
            .OfType<IDockable>()
            .FirstOrDefault();
        return dockable switch
        {
            IToolDock toolDock => DockTitle(toolDock),
            { Title: { Length: > 0 } } => dockable.Title,
            _ => "workbench panel",
        };
    }

    private static string DockTitle(IToolDock dock) =>
        string.IsNullOrWhiteSpace(dock.ActiveDockable?.Title)
            ? "workbench"
            : dock.ActiveDockable.Title;

    private static void SetAutomationName(Control control, string name)
    {
        if (!string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal))
        {
            AutomationProperties.SetName(control, name);
        }
    }

    internal bool FocusNextRegion()
    {
        string[] regions =
        [
            WorkbenchDockIds.FilesTool,
            WorkbenchDockIds.OverviewDocument,
            WorkbenchDockIds.GitTool,
            WorkbenchDockIds.ConversationTool,
            WorkbenchDockIds.RunOutputTool,
        ];
        focusRegionIndex = (focusRegionIndex + 1) % regions.Length;
        if (regions[focusRegionIndex] == WorkbenchDockIds.OverviewDocument)
        {
            IDockable target = layoutHost.Documents.ActiveDockable ?? layoutHost.Overview;
            factory.SetActiveDockable(target);
            FocusContext(target);
            return true;
        }

        return ActivateTool(regions[focusRegionIndex]);
    }

    private void FocusContext(IDockable dockable)
    {
        if (dockable.Context is not Control context)
        {
            return;
        }

        Control? target = context.Focusable
            ? context
            : context.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(item => item.Focusable && item.IsEffectivelyVisible);
        LastRequestedFocusTarget = target;
        if (target is not null && !target.Focus())
        {
            Dispatcher.UIThread.Post(() => target.Focus());
        }
    }

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
