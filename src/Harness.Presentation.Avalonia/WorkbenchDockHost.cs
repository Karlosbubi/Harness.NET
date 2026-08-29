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
using Harness.BusinessLogic.Coverage;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.Terminal;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;
using Harness.UI.Avalonia;
using AvaloniaOrientation = Avalonia.Layout.Orientation;
using DockAlignment = Dock.Model.Core.Alignment;
using DockOrientation = Dock.Model.Core.Orientation;

namespace Harness.Presentation.Avalonia;

internal enum GitWorkbenchSection
{
    Changes,
    Branches,
    Tags,
    Worktrees,
    Stashes,
    History,
    Conflicts,
    Remotes,
}

internal sealed partial class WorkbenchDockHost
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
    private readonly SolutionTool solutionTool;
    private readonly TestExplorerTool testExplorerTool;
    private readonly DebuggerTool debuggerToolUnit;
    private readonly GitChangesTool gitChangesTool;
    private readonly GitBranchesTool gitBranchesTool;
    private readonly GitWorktreesTool gitWorktreesTool;
    private readonly GitRemotesTool gitRemotesTool;
    private readonly GitHistoryTool gitHistoryTool;
    private readonly GitConflictsTool gitConflictsTool;
    private readonly RunOutputTool runOutputToolUnit;
    private readonly DeveloperTerminalTool terminalToolUnit;
    private readonly DocumentsHost documentsHost;
    private readonly ProblemsTool problemsToolUnit;
    private readonly WorkbenchOverview overviewHost;
    private readonly WorkbenchLayoutHost layoutHost;
    private readonly WorkbenchNavigator navigator;
    private readonly TabControl gitSections = new();
    private readonly TabControl workspaceSections = new();
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
        Func<string, Task>? manageWorkspaceAt = null,
        IDeveloperCoverageService? coverageService = null,
        IDeveloperDebuggerService? debuggerService = null,
        IDeveloperTerminalService? terminalService = null,
        ISensitiveDisplayGuard? sensitiveDisplayGuard = null)
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
        solutionTool = new(
            toolContext,
            developerExecutionService,
            () => { ShowRunOutput(); },
            RefreshRunOutputAsync);
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
        terminalToolUnit = new(terminalService, state, cancellationToken, sensitiveDisplayGuard);
        DebuggerTool? debuggerTool = null;
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
            debuggerService,
            session =>
            {
                ShowDebugger();
                return debuggerTool!.TrackAsync(session);
            },
            factory,
            cancellationToken);
        debuggerToolUnit = debuggerTool = new(
            debuggerService,
            documentsHost.NavigateToDebugAsync,
            cancellationToken);
        testExplorerTool = new(
            toolContext,
            codeIntelligenceService,
            developerExecutionService,
            documentsHost.NavigateToTestAsync,
            () => { ShowRunOutput(); },
            RefreshRunOutputAsync,
            coverageService,
            documentsHost.NavigateToCoverageAsync,
            debuggerService,
            session =>
            {
                ShowDebugger();
                return debuggerToolUnit.TrackAsync(session);
            });
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

        Control workspaceNavigation = BuildWorkspaceNavigation(navigation);
        Control files = filesTool.Content;
        Control sourceControl = BuildSourceControlTool();
        Control runOutput = runOutputToolUnit.Content;
        Control terminal = terminalToolUnit.Content;
        Control problemsContent = problemsToolUnit.Content;
        Control context = BuildContextTool(goalContext);
        Control overviewContent = overviewHost.Content;
        durableContexts.Add(WorkbenchDockIds.NavigationTool, workspaceNavigation);
        durableContexts.Add(WorkbenchDockIds.FilesTool, files);
        durableContexts.Add(WorkbenchDockIds.ContextTool, context);
        durableContexts.Add(WorkbenchDockIds.GitTool, sourceControl);
        durableContexts.Add(WorkbenchDockIds.ConversationTool, conversation);
        durableContexts.Add(WorkbenchDockIds.RunOutputTool, runOutput);
        durableContexts.Add(WorkbenchDockIds.TerminalTool, terminal);
        durableContexts.Add(WorkbenchDockIds.ProblemsTool, problemsContent);
        durableContexts.Add(WorkbenchDockIds.OverviewDocument, overviewContent);

        factory
            .Tool(out ITool? navigationTool, item => item
                .WithId(WorkbenchDockIds.NavigationTool)
                .WithTitle("Workspace")
                .WithCanClose(true)
                .WithContext(workspaceNavigation))
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
            .Tool(out ITool? terminalTool, item => item
                .WithId(WorkbenchDockIds.TerminalTool)
                .WithTitle("Terminal")
                .WithCanClose(true)
                .WithContext(terminal))
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
            runOutputTool!,
            terminalTool!);
        bottom.ActiveDockable = conversationTool;
        documents.VisibleDockables = factory.CreateList<IDockable>(overviewDocument);
        documents.ActiveDockable = overviewDocument;
        documentsHost.Attach(documents, overviewDocument);
        WorkbenchDockContent.Attach(navigationTool!, workspaceNavigation);
        WorkbenchDockContent.Attach(filesDockTool!, files);
        WorkbenchDockContent.Attach(contextTool!, context);
        WorkbenchDockContent.Attach(gitTool!, sourceControl);
        WorkbenchDockContent.Attach(conversationTool!, conversation);
        WorkbenchDockContent.Attach(runOutputTool!, runOutput);
        WorkbenchDockContent.Attach(terminalTool!, terminal);
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

    internal bool ShowSolution()
    {
        bool shown = navigator.ShowWorkspace();
        workspaceSections.SelectedIndex = 1;
        return shown;
    }

    internal bool ShowTestExplorer()
    {
        bool shown = navigator.ShowWorkspace();
        workspaceSections.SelectedIndex = 2;
        return shown;
    }

    internal bool ShowDebugger()
    {
        bool shown = navigator.ShowWorkspace();
        workspaceSections.SelectedIndex = 3;
        return shown;
    }

    internal bool ShowConversation() => navigator.ShowConversation();

    internal bool ShowGit() => navigator.ShowGit();

    internal bool ShowGit(GitWorkbenchSection section)
    {
        bool shown = navigator.ShowGit();
        gitSections.SelectedIndex = (int)section;
        return shown;
    }

    internal string GitStatusText => GitStatus.Text ?? string.Empty;

    internal string GitSummaryText => gitChangesTool.Summary.Text ?? string.Empty;

    internal bool ShowRunOutput() => navigator.ShowRunOutput();

    internal bool ShowTerminal() => navigator.ShowTerminal();

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
