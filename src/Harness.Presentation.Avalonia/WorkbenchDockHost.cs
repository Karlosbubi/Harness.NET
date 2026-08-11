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
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;
using AvaloniaOrientation = Avalonia.Layout.Orientation;
using DockAlignment = Dock.Model.Core.Alignment;
using DockOrientation = Dock.Model.Core.Orientation;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkbenchDockHost
{
    private readonly IRunOutputService runOutputService;
    private readonly IWorkbenchInspectionService inspectionService;
    private readonly IWorkbenchDocumentService documentService;
    private readonly IWorkbenchCodeIntelligenceService codeIntelligenceService;
    private readonly IWorkspaceMutationService? mutationService;
    private readonly IWorkbenchLayoutService layoutService;
    private readonly IWorkbenchDocumentPrompt documentPrompt;
    private readonly Func<AvaloniaShellState> state;
    private readonly Func<bool, Task> manageWorkspace;
    private readonly CancellationToken cancellationToken;
    private readonly Factory factory = new();
    private readonly WorkbenchDockLayoutCodec layoutCodec;
    private readonly Dictionary<string, Control> durableContexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceDocumentSession> sourceDocuments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkbenchCodeDiagnosticView> documentDiagnostics =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim codeSessionGate = new(1, 1);
    private readonly TextBlock layoutStatus = new()
    {
        MaxWidth = 180,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ComboBox documentSwitcher = new()
    {
        MinWidth = 170,
        MaxWidth = 260,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private IDocumentDock documents = null!;
    private IToolDock leftTools = null!;
    private IToolDock rightTools = null!;
    private IToolDock bottomTools = null!;
    private IDockable overviewDocument = null!;
    private IRootDock root = null!;
    private string defaultLayoutPayload = string.Empty;
    private readonly TextBlock overviewHeading = new()
    {
        FontSize = 22,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock overviewDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button overviewAction = new() { Content = "Open workspace" };
    private readonly TextBox fileFilter = new();
    private readonly TreeView fileTree = new();
    private readonly TextBox query = new();
    private readonly ListBox searchResults = new();
    private readonly TextBlock fileStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox changes = new();
    private readonly TextBlock gitSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock gitStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox runOutputs = new();
    private readonly TextBlock runOutputStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextEditor runOutputDetails = CodeEditorView.Create(
        string.Empty,
        isReadOnly: true,
        wordWrap: false,
        showLineNumbers: false,
        path: "run-output.txt");
    private readonly ListBox problems = new();
    private readonly TextBlock problemsStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox showWarnings = new() { Content = "Warnings", IsChecked = true };
    private readonly CheckBox showInformation = new() { Content = "Info", IsChecked = true };
    private readonly CheckBox showHidden = new() { Content = "Hidden", IsChecked = false };
    private string? workspaceId;
    private string? selectedGoalId;
    private string? runOutputFingerprint;
    private bool busy;
    private bool runOutputBusy;
    private bool fileTreeBusy;
    private int fileTreeContextVersion;
    private IReadOnlyList<WorkbenchDocumentPath> trackedFiles = [];
    private string? fileTreeError;
    private string? fileTreeContext;
    private bool fileTreeTruncated;
    private bool suppressDocumentActivation;
    private bool renderingDocumentSwitcher;
    private bool resolvingDocumentTransition;
    private bool adaptiveLeftCollapsed;
    private bool adaptiveRightCollapsed;
    private bool adaptiveBottomCollapsed;
    private double expandedLeftProportion = 0.19;
    private double expandedRightProportion = 0.22;
    private double expandedBottomProportion = 0.45;
    private bool viewportInitialized;
    private int focusRegionIndex = -1;
    private IDockable? activeDocument;
    private WorkbenchCodeSessionId? codeSessionId;
    private string? codeSessionKey;

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
        IWorkspaceMutationService? mutationService = null)
    {
        this.runOutputService = runOutputService;
        this.inspectionService = inspectionService;
        this.documentService = documentService;
        this.codeIntelligenceService = codeIntelligenceService;
        this.mutationService = mutationService;
        this.layoutService = layoutService;
        this.documentPrompt = documentPrompt;
        this.state = state;
        this.manageWorkspace = manageWorkspace ?? (_ => Task.CompletedTask);
        this.cancellationToken = cancellationToken;
        factory.HideToolsOnClose = true;
        layoutCodec = new(factory);

        Control files = BuildFilesTool();
        Control sourceControl = BuildSourceControlTool();
        Control runOutput = BuildRunOutputTool();
        Control problemsContent = BuildProblemsTool();
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
            .Tool(out ITool? filesTool, item => item
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

        documents = documentDock ?? throw new InvalidOperationException("Dock did not create the document region.");
        leftTools = left;
        rightTools = right;
        bottomTools = bottom;
        overviewDocument = overview ?? throw new InvalidOperationException("Dock did not create the overview document.");
        left!.WithProportion(0.19);
        right!.WithProportion(0.22);
        bottom!.WithProportion(0.45);
        root = rootDock ?? throw new InvalidOperationException("Dock did not create the workbench root.");
        left.VisibleDockables = factory.CreateList<IDockable>(navigationTool!, filesTool!);
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
        WorkbenchDockContent.Attach(navigationTool!, navigation);
        WorkbenchDockContent.Attach(filesTool!, files);
        WorkbenchDockContent.Attach(contextTool!, context);
        WorkbenchDockContent.Attach(gitTool!, sourceControl);
        WorkbenchDockContent.Attach(conversationTool!, conversation);
        WorkbenchDockContent.Attach(runOutputTool!, runOutput);
        WorkbenchDockContent.Attach(problemsTool!, problemsContent);
        WorkbenchDockContent.Attach(overviewDocument, overviewContent);
        EnsureDefaultTools(left, right, bottom, "before Dock initialization");
        factory.InitLayout(root);
        EnsureDefaultTools(left, right, bottom, "after Dock initialization");
        left.IsExpanded = true;
        right.IsExpanded = true;
        bottom.IsExpanded = true;
        activeDocument = overviewDocument;
        factory.ActiveDockableChanged += OnActiveDockableChanged;
        factory.DockableClosed += OnDockableClosed;
        factory.WindowAdded += (_, args) =>
        {
            if (args.Window is { } window)
            {
                window.OwnerMode = DockWindowOwnerMode.DockableWindow;
                window.ShowInTaskbar = false;
            }
        };
        WorkbenchDockLayoutCaptureResult defaultLayout = layoutCodec.Capture(root);
        defaultLayoutPayload = defaultLayout.Payload ?? throw new InvalidOperationException(
            $"Dock did not create a valid default layout: {defaultLayout.Error}");

        Control = new DockControl
        {
            Factory = factory,
            Layout = root,
            Focusable = true,
        };
        AutomationProperties.SetName(Control, "Docked workspace workbench");
        Control.KeyDown += OnWorkbenchKeyDown;
        Control.SizeChanged += (_, _) => ApplyViewport(Control.Bounds.Width, Control.Bounds.Height);
        Control.LayoutUpdated += (_, _) => ApplyDockAutomationNames();
        LayoutActions = BuildLayoutActions();
        DocumentActions = BuildDocumentActions();
    }

    internal DockControl Control { get; }
    internal Control LayoutActions { get; }
    internal Control DocumentActions { get; }
    internal ComboBox DocumentSwitcher => documentSwitcher;
    internal Button OverviewAction => overviewAction;
    internal IDocumentDock Documents => documents;
    internal IRootDock Root => root;
    internal IFactory Factory => factory;
    internal string? LayoutStatusText => layoutStatus.Text;
    internal bool IsCompactViewport { get; private set; }
    internal Control? LastRequestedFocusTarget { get; private set; }
    internal int SourceDocumentCount => sourceDocuments.Count;
    internal TreeView FileTree => fileTree;
    internal TextBox FileFilter => fileFilter;
    internal TextEditor? ActiveSourceEditor => activeDocument?.Id is { } id &&
                                               sourceDocuments.TryGetValue(id, out SourceDocumentSession? session)
        ? session.Editor
        : null;
    internal ListBox Problems => problems;
    internal string? ProblemsStatusText => problemsStatus.Text;
    internal bool ActiveSourceDocumentIsDirty => activeDocument?.Id is { } id &&
                                                 sourceDocuments.TryGetValue(id, out SourceDocumentSession? session) &&
                                                 session.IsDirty;
    internal IReadOnlyList<InboundOpenDocumentView> InboundOpenDocuments =>
        sourceDocuments.Values.Select(session => new InboundOpenDocumentView(
            session.View.Path.Value,
            session.View.GoalId?.Value,
            session.View.Sha256?.Value,
            session.CurrentBufferVersion,
            session.IsDirty,
            session.View.Access is WorkbenchDocumentAccess.Editable,
            ReferenceEquals(activeDocument, session.Document))).ToArray();

    internal int ActiveCompletionItemCount => activeDocument?.Id is { } completionId &&
                                               sourceDocuments.TryGetValue(
                                                   completionId,
                                                   out SourceDocumentSession? completionSession)
        ? completionSession.CompletionWindow?.CompletionList.CompletionData.Count ?? 0
        : 0;

    internal CompletionWindow? ActiveCompletionWindow => activeDocument?.Id is { } windowId &&
                                                          sourceDocuments.TryGetValue(
                                                              windowId,
                                                              out SourceDocumentSession? windowSession)
        ? windowSession.CompletionWindow
        : null;

    internal bool ActiveQuickInfoIsOpen => activeDocument?.Id is { } quickInfoId &&
                                           sourceDocuments.TryGetValue(
                                               quickInfoId,
                                               out SourceDocumentSession? quickInfoSession) &&
                                           quickInfoSession.QuickInfoWindow?.IsVisible is true;

    internal ValueTask<bool> SaveActiveSourceDocumentAsync() =>
        activeDocument?.Id is { } id &&
        sourceDocuments.TryGetValue(id, out SourceDocumentSession? session)
            ? SaveSourceDocumentAsync(session)
            : ValueTask.FromResult(false);

    internal ValueTask CloseActiveSourceDocumentAsync() =>
        activeDocument?.Id is { } id &&
        sourceDocuments.TryGetValue(id, out SourceDocumentSession? session)
            ? RequestSourceDocumentCloseAsync(session)
            : ValueTask.CompletedTask;

    internal async ValueTask RestoreLayoutAsync()
    {
        WorkbenchLayoutLoadResult stored = await layoutService.LoadAsync(cancellationToken);
        if (stored.State is WorkbenchLayoutLoadState.Missing)
        {
            layoutStatus.Text = "Default layout";
            layoutStatus.IsVisible = false;
            return;
        }

        if (stored.Layout is null)
        {
            layoutStatus.Text = $"Saved layout rejected · {stored.Error ?? "invalid private state"}";
            layoutStatus.IsVisible = true;
            return;
        }

        WorkbenchDockLayoutRestoreResult restored = layoutCodec.Restore(
            stored.Layout.Value,
            durableContexts,
            WorkingArea());
        if (restored.Layout is null || restored.Documents is null || restored.Overview is null)
        {
            layoutStatus.Text = $"Saved layout rejected · {restored.Error ?? "invalid Dock graph"}";
            layoutStatus.IsVisible = true;
            return;
        }

        ApplyLayout(restored.Layout, restored.Documents, restored.Overview);
        layoutStatus.Text = "Layout restored";
        layoutStatus.IsVisible = true;
    }

    internal async ValueTask SaveLayoutAsync(
        CancellationToken saveCancellationToken = default)
    {
        WorkbenchDockLayoutCaptureResult captured = layoutCodec.Capture(root);
        if (captured.Payload is null)
        {
            layoutStatus.Text = $"Layout not saved · {captured.Error ?? "invalid Dock graph"}";
            layoutStatus.IsVisible = true;
            return;
        }

        WorkbenchLayoutWriteResult result = await layoutService.SaveAsync(
            new(captured.Payload),
            saveCancellationToken);
        layoutStatus.Text = result.Succeeded
            ? "Layout saved"
            : $"Layout not saved · {result.Error ?? "private state unavailable"}";
        layoutStatus.IsVisible = true;
    }

    internal async ValueTask ResetLayoutAsync()
    {
        if (!await CloseAllSourceDocumentsAsync(WorkbenchDocumentTransition.Close))
        {
            layoutStatus.Text = "Layout reset cancelled · unsaved source changes kept";
            return;
        }

        WorkbenchLayoutWriteResult reset = await layoutService.ResetAsync(cancellationToken);
        WorkbenchDockLayoutRestoreResult restored = layoutCodec.Restore(
            defaultLayoutPayload,
            durableContexts,
            WorkingArea());
        if (restored.Layout is null || restored.Documents is null || restored.Overview is null)
        {
            layoutStatus.Text = $"Layout reset failed · {restored.Error ?? "invalid default layout"}";
            return;
        }

        ApplyLayout(restored.Layout, restored.Documents, restored.Overview);
        layoutStatus.Text = reset.Succeeded
            ? "Default layout restored"
            : $"Default active; stored layout not removed · {reset.Error}";
        layoutStatus.IsVisible = true;
    }

    internal async ValueTask RefreshAsync()
    {
        Update(state());
        if (ActiveWorkspace() is { IsTrusted: true })
        {
            await RefreshFilesAsync();
            await RefreshGitAsync();
        }
    }

    internal async ValueTask<bool> PrepareForShutdownAsync()
    {
        foreach (SourceDocumentSession session in sourceDocuments.Values
                     .Where(item => item.IsDirty)
                     .ToArray())
        {
            if (!await ResolveUnsavedAsync(
                    session,
                    WorkbenchDocumentTransition.Exit,
                    discardKeepsDocument: true))
            {
                return false;
            }
        }

        return true;
    }

    internal ValueTask<bool> PrepareForWorkspaceChangeAsync() =>
        CloseAllSourceDocumentsAsync(WorkbenchDocumentTransition.Switch);

    internal void Update(AvaloniaShellState snapshot)
    {
        foreach (SourceDocumentSession session in sourceDocuments.Values)
        {
            CodeEditorView.ApplyTheme(session.Editor);
        }

        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        if (!string.Equals(workspaceId, active?.Id, StringComparison.Ordinal))
        {
            fileTreeContextVersion++;
            workspaceId = active?.Id;
            Dispatcher.UIThread.Post(async () =>
                await CloseAllSourceDocumentsAsync(WorkbenchDocumentTransition.Close));
            Dispatcher.UIThread.Post(async () => await InvalidateCodeIntelligenceAsync());
            searchResults.ItemsSource = Array.Empty<SearchChoice>();
            searchResults.IsVisible = false;
            trackedFiles = [];
            fileTreeError = null;
            fileTreeContext = null;
            fileTreeTruncated = false;
            RenderFileTree();
            changes.ItemsSource = Array.Empty<ChangeChoice>();
            fileStatus.Text = string.Empty;
            gitStatus.Text = string.Empty;
            gitSummary.Text = active is null ? "No workspace selected." : "Refresh Git state.";
            if (active is { IsTrusted: true })
            {
                Dispatcher.UIThread.Post(async () => await RefreshFilesAsync());
            }
        }

        GoalView? selectedGoal = snapshot.Goals.SelectedGoal;
        string? nextGoalId = selectedGoal is not null && active is not null &&
                             selectedGoal.WorkspaceId == active.Id
            ? selectedGoal.Id.Value
            : null;
        if (!string.Equals(selectedGoalId, nextGoalId, StringComparison.Ordinal))
        {
            fileTreeContextVersion++;
            selectedGoalId = nextGoalId;
            Dispatcher.UIThread.Post(async () => await InvalidateCodeIntelligenceAsync());
            searchResults.ItemsSource = Array.Empty<SearchChoice>();
            searchResults.IsVisible = false;
            changes.ItemsSource = Array.Empty<ChangeChoice>();
            fileStatus.Text = nextGoalId is null
                ? "Source context: original workspace."
                : "Source context changed; refreshing the selected goal worktree.";
            gitStatus.Text = string.Empty;
            gitSummary.Text = active is null
                ? "No workspace selected."
                : "Refreshing Git state for the current source context…";
            if (active is { IsTrusted: true })
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await RefreshFilesAsync();
                    await RefreshGitAsync();
                });
            }
        }

        string nextRunOutputFingerprint = $"{nextGoalId}|{snapshot.Goals.Workflow?.State}|" +
                                          $"{snapshot.Goals.Workflow?.Activities.Count ?? 0}|" +
                                          snapshot.Goals.IsWorkflowRunning;
        if (!string.Equals(runOutputFingerprint, nextRunOutputFingerprint, StringComparison.Ordinal))
        {
            runOutputFingerprint = nextRunOutputFingerprint;
            Dispatcher.UIThread.Post(async () => await RefreshRunOutputAsync());
        }

        if (active is null)
        {
            overviewHeading.Text = "Open a repository to get started";
            overviewDetails.Text = "Choose a Git-backed .NET repository. Harness.NET will discover its solutions and projects before asking you to trust it.";
            overviewAction.Content = "Open workspace";
            overviewAction.Classes.Remove("command");
            overviewAction.Classes.Add("primary");
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
    }

    internal ValueTask OpenFileAsync(string relativePath) =>
        OpenFileAsync(relativePath, state().Goals.SelectedGoal?.Id);

    internal async ValueTask<InboundUiActionResult> OpenInboundDocumentAsync(
        InboundUiDocumentRequest request)
    {
        GoalId? goalId = string.IsNullOrWhiteSpace(request.GoalId) ? null : new(request.GoalId);
        await OpenFileAsync(request.RelativePath, goalId);
        SourceDocumentSession? opened = sourceDocuments.Values.FirstOrDefault(item =>
            item.View.Path.Value.Equals(request.RelativePath, StringComparison.Ordinal) &&
            item.View.GoalId == goalId);
        return opened is not null
            ? new(new("document.open"), true, null, null)
            : new(new("document.open"), false, "document_open_failed", fileStatus.Text);
    }

    /// <summary>
    /// Offers each Git-tracked file as a command that opens it. The catalog is loaded on
    /// demand so quick open reflects the same bounded, context-resolved file list the
    /// Files panel shows rather than a separate scan.
    /// </summary>
    internal async ValueTask<IReadOnlyList<PaletteCommand>> BuildFileCommandsAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (active is null || !active.IsTrusted)
        {
            return [];
        }

        if (trackedFiles.Count == 0 && fileTreeError is null)
        {
            await RefreshFilesAsync();
        }

        return
        [
            .. trackedFiles.Select(path => new PaletteCommand(
                $"file:{path.Value}",
                Directory(path.Value),
                Name(path.Value),
                () => OpenFileAsync(path.Value),
                MatchText: path.Value))
        ];
    }

    private static string Directory(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator <= 0 ? "(repository root)" : path[..separator];
    }

    private static string Name(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private async ValueTask OpenFileAsync(string relativePath, GoalId? requestedGoalId)
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted || string.IsNullOrWhiteSpace(relativePath))
        {
            fileStatus.Text = active is null
                ? "Select a workspace first."
                : active.IsTrusted ? "Enter a relative file path." : "Trust the workspace before reading files.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkbenchDocumentView file = await documentService.OpenAsync(
                new(
                    new(active.Id),
                    requestedGoalId,
                    new(relativePath.Trim())),
                cancellationToken);
            if (file.Error is not null)
            {
                fileStatus.Text = file.Error;
                return;
            }

            string id = SourceDocumentId(file);
            if (sourceDocuments.TryGetValue(id, out SourceDocumentSession? existing))
            {
                if (await TrySwitchDocumentAsync(existing.Document))
                {
                    fileStatus.Text = $"Activated {file.Path.Value}.";
                }

                return;
            }

            if (!await PrepareActiveDocumentTransitionAsync(WorkbenchDocumentTransition.Switch))
            {
                fileStatus.Text = $"Kept unsaved changes; {file.Path.Value} was not opened.";
                return;
            }

            SourceDocumentSession session = CreateSourceDocument(id, file);
            documents.AddDocument(session.Document);
            SetActiveDocument(session.Document);
            fileStatus.Text = $"Opened {file.Path.Value} · {file.Size.Value:N0} bytes · " +
                              file.AccessDescription.TrimEnd('.') +
                              (file.IsTruncated ? " · truncated." : ".");
        });
    }

    internal async ValueTask RefreshGitAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted)
        {
            gitStatus.Text = active is null
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
                gitStatus.Text = git.Error;
                return;
            }

            int conflictCount = git.Changes.Count(change =>
                change.Status.Contains("Conflicted", StringComparison.OrdinalIgnoreCase));
            gitSummary.Text = $"{inspected.Context.Description}\nBranch {git.Branch}\n" +
                              $"HEAD {git.HeadSha ?? "unborn"}\n" +
                              $"{git.Changes.Count} change(s)" +
                              (conflictCount > 0 ? $" · {conflictCount} conflict(s)" : string.Empty) +
                              (git.IsTruncated ? " · truncated" : string.Empty);
            changes.ItemsSource = git.Changes
                .Select(change => new ChangeChoice(change, inspected.Context.GoalId))
                .ToArray();
            gitStatus.Text = conflictCount > 0
                ? $"{conflictCount} unresolved Git conflict(s) block commit approval. " +
                  "Resolve and stage them with Git, then refresh this view."
                : "Git state refreshed.";
        });
    }

    internal async ValueTask OpenDiffAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted)
        {
            gitStatus.Text = active is null
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
                gitStatus.Text = git.Error;
                return;
            }

            if (string.IsNullOrWhiteSpace(git.Diff))
            {
                gitStatus.Text = "The working tree has no textual diff.";
                return;
            }

            OpenOrReplaceDocument(
                DiffDocumentId(inspected.Context),
                $"{git.Branch} working diff",
                CreateDiffView(git.Diff));
            gitStatus.Text = $"Opened the current bounded Git diff · {inspected.Context.Description}.";
        });
    }

    internal void OpenPlan()
    {
        if (state().Goals.CurrentPlan is not { } plan)
        {
            overviewDetails.Text = "The selected goal has no current plan to open.";
            ActivateOverview();
            return;
        }

        OpenOrReplaceDocument(
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
            ActivateOverview();
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

        OpenOrReplaceDocument(
            WorkbenchDockIds.EvidenceDocument,
            "Workflow evidence",
            new ScrollViewer { Content = content, Padding = new Thickness(18) });
    }

    private Control BuildLayoutActions()
    {
        Button save = new() { Content = "↓" };
        AutomationProperties.SetName(save, "Save current panel layout");
        ToolTip.SetTip(save, "Save panel layout");
        save.Classes.Add("icon");
        save.Click += async (_, _) => await SaveLayoutAsync(cancellationToken);
        Button reset = new() { Content = "↺" };
        AutomationProperties.SetName(reset, "Reset panels to the default layout");
        ToolTip.SetTip(reset, "Reset panel layout");
        reset.Classes.Add("icon");
        reset.Click += async (_, _) => await ResetLayoutAsync();
        AutomationProperties.SetName(layoutStatus, "Workbench layout status");
        layoutStatus.Text = "Default layout";
        layoutStatus.IsVisible = false;
        return new StackPanel
        {
            Orientation = AvaloniaOrientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { save, reset },
        };
    }

    private Control BuildDocumentActions()
    {
        AutomationProperties.SetName(documentSwitcher, "Open editor documents");
        documentSwitcher.SelectionChanged += async (_, _) =>
        {
            if (renderingDocumentSwitcher ||
                documentSwitcher.SelectedItem is not DocumentChoice choice)
            {
                return;
            }

            if (!await TrySwitchDocumentAsync(choice.Document))
            {
                UpdateDocumentSwitcher();
            }
        };
        UpdateDocumentSwitcher();
        Button focusEditor = new() { Content = "Focus editor" };
        AutomationProperties.SetName(focusEditor, "Focus the active editor document");
        focusEditor.Click += (_, _) => FocusActiveEditor();
        StackPanel actions = new()
        {
            Orientation = AvaloniaOrientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Document",
                    VerticalAlignment = VerticalAlignment.Center,
                },
                documentSwitcher,
                focusEditor,
                layoutStatus,
            },
        };
        AutomationProperties.SetName(actions, "Editor document navigation");
        return actions;
    }

    private void UpdateDocumentSwitcher()
    {
        renderingDocumentSwitcher = true;
        try
        {
            DocumentChoice[] choices = documents.VisibleDockables?
                .Where(IsDocument)
                .Select(item => new DocumentChoice(item))
                .ToArray() ?? [];
            documentSwitcher.ItemsSource = choices;
            documentSwitcher.SelectedItem = choices.FirstOrDefault(item =>
                ReferenceEquals(item.Document, activeDocument));
        }
        finally
        {
            renderingDocumentSwitcher = false;
        }
    }

    private void FocusActiveEditor()
    {
        OwnerWindow()?.Activate();
        if (ActiveSourceEditor is { } editor)
        {
            LastRequestedFocusTarget = editor;
            if (!editor.Focus())
            {
                Dispatcher.UIThread.Post(() => editor.Focus());
            }
            return;
        }

        if (activeDocument is { } document)
        {
            FocusContext(document);
        }
    }

    private static void EnsureDefaultTools(
        IToolDock left,
        IToolDock right,
        IToolDock bottom,
        string stage)
    {
        if (left.VisibleDockables?.Count != 2 || right.VisibleDockables?.Count != 2 ||
            bottom.VisibleDockables?.Count != 3)
        {
            throw new InvalidOperationException($"Dock lost the default tool panels {stage}.");
        }
    }

    private void ApplyLayout(
        IRootDock restored,
        IDocumentDock restoredDocuments,
        IDockable restoredOverview)
    {
        root.ExitWindows?.Execute(null);
        // Dock's retired deferred presenters otherwise keep direct Control content parented,
        // which prevents the replacement graph from materializing the same durable controls.
        foreach (Control context in durableContexts.Values)
        {
            WorkbenchDockContent.ReleaseFromPresenter(context);
        }

        // Restore builds the replacement Dock graph while the durable controls can
        // still belong to presenters in the retiring graph. Re-apply the rendered
        // content contract after releasing those presenters so complex controls
        // (notably Conversation) are materialized by the replacement graph.
        foreach ((string id, Control context) in durableContexts)
        {
            if (FindDockable(restored, id) is { } dockable)
            {
                WorkbenchDockContent.Attach(dockable, context);
            }
        }

        Control.Layout = null;
        root = restored;
        documents = restoredDocuments;
        overviewDocument = restoredOverview;
        leftTools = FindDockable<IToolDock>(root, WorkbenchDockIds.Left);
        rightTools = FindDockable<IToolDock>(root, WorkbenchDockIds.Right);
        bottomTools = FindDockable<IToolDock>(root, WorkbenchDockIds.Bottom);
        SetDockContentVisibility(leftTools, visible: true);
        SetDockContentVisibility(rightTools, visible: true);
        SetDockContentVisibility(bottomTools, visible: true);
        adaptiveLeftCollapsed = false;
        adaptiveRightCollapsed = false;
        adaptiveBottomCollapsed = false;
        factory.InitLayout(root);
        Control.Layout = root;
        root.ShowWindows?.Execute(null);
        IDockable restoredActiveDocument = documents.ActiveDockable ?? overviewDocument;
        factory.SetActiveDockable(restoredActiveDocument);
        activeDocument = restoredActiveDocument;
        UpdateDocumentSwitcher();
        viewportInitialized = true;
        ApplyViewport(Control.Bounds.Width, Control.Bounds.Height);
    }

    internal void ApplyViewport(double width, double height)
    {
        bool compact = width > 0 && width < 1024;
        bool narrow = width > 0 && width < 840;
        // This receives the inner workbench height, after the shell header, footer, and
        // document toolbar. Keep the primary conversation visible at a normal 800px
        // window and collapse it only near the minimum-height layout.
        bool shortViewport = height > 0 && height < 560;
        IsCompactViewport = compact || shortViewport;
        if (!viewportInitialized && width > 0 && height > 0)
        {
            leftTools.IsExpanded = !narrow;
            rightTools.IsExpanded = !compact;
            bottomTools.IsExpanded = !shortViewport;
            if (narrow)
            {
                leftTools.Proportion = 0.06;
                leftTools.CollapsedProportion = 0.06;
                leftTools.MaxWidth = 76;
                SetDockContentVisibility(leftTools, visible: false);
            }
            if (compact)
            {
                rightTools.Proportion = 0.06;
                rightTools.CollapsedProportion = 0.06;
                rightTools.MaxWidth = 76;
                SetDockContentVisibility(rightTools, visible: false);
            }
            if (shortViewport)
            {
                bottomTools.Proportion = 0.08;
                bottomTools.CollapsedProportion = 0.08;
                bottomTools.MaxHeight = 84;
                SetDockContentVisibility(bottomTools, visible: false);
            }
            adaptiveLeftCollapsed = narrow;
            adaptiveRightCollapsed = compact;
            adaptiveBottomCollapsed = shortViewport;
            viewportInitialized = true;
            return;
        }

        SetAdaptiveExpansion(
            leftTools, narrow, 0.06, constrainWidth: true,
            ref adaptiveLeftCollapsed, ref expandedLeftProportion);
        SetAdaptiveExpansion(
            rightTools, compact, 0.06, constrainWidth: true,
            ref adaptiveRightCollapsed, ref expandedRightProportion);
        SetAdaptiveExpansion(
            bottomTools, shortViewport, 0.08, constrainWidth: false,
            ref adaptiveBottomCollapsed, ref expandedBottomProportion);
    }

    private static void SetAdaptiveExpansion(
        IToolDock dock,
        bool collapse,
        double collapsedProportion,
        bool constrainWidth,
        ref bool adaptivelyCollapsed,
        ref double expandedProportion)
    {
        if (collapse && !adaptivelyCollapsed)
        {
            if (double.IsFinite(dock.Proportion) && dock.Proportion > 0)
            {
                expandedProportion = dock.Proportion;
            }
            dock.Proportion = collapsedProportion;
            dock.CollapsedProportion = collapsedProportion;
            if (constrainWidth)
            {
                dock.MaxWidth = 76;
            }
            else
            {
                dock.MaxHeight = 84;
            }
            SetDockContentVisibility(dock, visible: false);
            dock.IsExpanded = false;
            adaptivelyCollapsed = true;
        }
        else if (!collapse && adaptivelyCollapsed)
        {
            dock.Proportion = expandedProportion;
            dock.CollapsedProportion = expandedProportion;
            dock.MaxWidth = double.PositiveInfinity;
            dock.MaxHeight = double.PositiveInfinity;
            SetDockContentVisibility(dock, visible: true);
            dock.IsExpanded = true;
            adaptivelyCollapsed = false;
        }
    }

    private PixelRect WorkingArea()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(Control);
        return topLevel?.Screens?.ScreenFromVisual(Control)?.WorkingArea ??
               new PixelRect(0, 0, 1920, 1080);
    }

    private Control BuildFilesTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto,Auto"),
            Margin = new Thickness(8),
            RowSpacing = 6,
        };
        Grid filterRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
        fileFilter.PlaceholderText = "Filter repository files";
        fileFilter.Classes.Add("workspace-input");
        AutomationProperties.SetName(fileFilter, "Filter repository file tree");
        fileFilter.TextChanged += (_, _) => RenderFileTree();
        AccessibleIconButton refresh = new()
        {
            Content = "↻",
            AccessibleName = "Refresh repository file tree",
        };
        refresh.Classes.Add("icon");
        refresh.Click += async (_, _) => await RefreshFilesAsync();
        filterRow.Children.Add(fileFilter);
        Grid.SetColumn(refresh, 1);
        filterRow.Children.Add(refresh);
        grid.Children.Add(filterRow);

        fileTree.ItemTemplate = new FuncTreeDataTemplate<FileTreeNode>(
            (node, _) =>
            {
                if (node.Path is { } filePath)
                {
                    Button file = new()
                    {
                        Content = node.Name,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    file.Classes.Add("tree-file");
                    AutomationProperties.SetName(file, filePath.Value);
                    file.Click += async (_, _) => await OpenFileAsync(filePath.Value);
                    return file;
                }

                TextBlock label = new()
                {
                    Text = node.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                return label;
            },
            node => node.Children);
        AutomationProperties.SetName(fileTree, "Repository file tree");
        Grid.SetRow(fileTree, 1);
        grid.Children.Add(fileTree);

        Grid searchRow = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        query.PlaceholderText = "Search file contents";
        query.Classes.Add("workspace-input");
        AutomationProperties.SetName(query, "Search tracked workspace text");
        AccessibleIconButton search = new()
        {
            Content = "⌕",
            AccessibleName = "Run tracked workspace search",
        };
        search.Classes.Add("icon");
        AutomationProperties.SetName(search, "Run tracked workspace search");
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Enter)
            {
                args.Handled = true;
                await SearchAsync();
            }
        };
        searchRow.Children.Add(query);
        Grid.SetColumn(search, 1);
        searchRow.Children.Add(search);
        Grid.SetRow(searchRow, 2);
        grid.Children.Add(searchRow);

        AutomationProperties.SetName(searchResults, "Tracked-text search results");
        searchResults.MaxHeight = 180;
        searchResults.IsVisible = false;
        searchResults.DoubleTapped += async (_, _) =>
        {
            if (searchResults.SelectedItem is SearchChoice choice)
            {
                await OpenFileAsync(choice.Match.Path, choice.GoalId);
            }
        };
        Grid.SetRow(searchResults, 3);
        grid.Children.Add(searchResults);
        fileStatus.Classes.Add("muted");
        Grid.SetRow(fileStatus, 4);
        grid.Children.Add(fileStatus);
        return grid;
    }

    internal async ValueTask RefreshFilesAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (fileTreeBusy)
        {
            return;
        }

        if (active is null || !active.IsTrusted)
        {
            trackedFiles = [];
            fileTreeError = active is null
                ? "Select a workspace to browse its files."
                : "Trust the workspace to browse its files.";
            fileTreeTruncated = false;
            fileTreeContext = null;
            RenderFileTree();
            return;
        }

        int contextVersion = fileTreeContextVersion;
        fileTreeBusy = true;
        fileTreeError = null;
        fileStatus.Text = "Loading repository files…";
        try
        {
            WorkbenchFileCatalogResult result = await inspectionService.ListFilesAsync(
                WorkbenchRequest(active),
                cancellationToken);
            if (contextVersion != fileTreeContextVersion)
            {
                return;
            }

            trackedFiles = result.Catalog.Files;
            fileTreeTruncated = result.Catalog.IsTruncated;
            fileTreeError = result.Catalog.Error;
            fileTreeContext = result.Context.Description;
            RenderFileTree();
        }
        catch (OperationCanceledException)
        {
            if (contextVersion == fileTreeContextVersion)
            {
                fileTreeError = "Repository file loading was cancelled.";
                RenderFileTree();
            }
        }
        catch (Exception exception)
        {
            if (contextVersion == fileTreeContextVersion)
            {
                fileTreeError = exception.Message;
                RenderFileTree();
            }
        }
        finally
        {
            fileTreeBusy = false;
            if (contextVersion != fileTreeContextVersion)
            {
                Dispatcher.UIThread.Post(async () => await RefreshFilesAsync());
            }
        }
    }

    private void RenderFileTree()
    {
        string filter = fileFilter.Text?.Trim() ?? string.Empty;
        IReadOnlyList<WorkbenchDocumentPath> visible = filter.Length == 0
            ? trackedFiles
            : trackedFiles
                .Where(file => file.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        fileTree.ItemsSource = BuildFileTree(visible);
        fileStatus.Text = fileTreeError ?? (trackedFiles.Count == 0
            ? "No tracked files are available in the current source context."
            : filter.Length == 0
                ? $"{trackedFiles.Count:N0} tracked files" +
                  (fileTreeTruncated ? " · list truncated" : string.Empty) +
                  (string.IsNullOrWhiteSpace(fileTreeContext) ? string.Empty : $" · {fileTreeContext}")
                : $"{visible.Count:N0} of {trackedFiles.Count:N0} tracked files");
    }

    private static IReadOnlyList<FileTreeNode> BuildFileTree(
        IReadOnlyList<WorkbenchDocumentPath> paths)
    {
        FileTreeBuilder root = new(string.Empty);
        foreach (WorkbenchDocumentPath path in paths)
        {
            string[] segments = path.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            FileTreeBuilder parent = root;
            foreach (string directory in segments[..^1])
            {
                parent = parent.Directory(directory);
            }

            parent.Files.Add(new(segments[^1], path, []));
        }

        return root.ToNodes();
    }

    private Control BuildSourceControlTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            Margin = new Thickness(10),
            RowSpacing = 8,
        };
        grid.Children.Add(gitSummary);
        StackPanel actions = new()
        {
            Orientation = AvaloniaOrientation.Horizontal,
            Spacing = 6,
        };
        Button refresh = new() { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Refresh Git working-tree state");
        refresh.Click += async (_, _) => await RefreshGitAsync();
        Button openDiff = new() { Content = "Open diff" };
        AutomationProperties.SetName(openDiff, "Open bounded Git working-tree diff");
        openDiff.Click += async (_, _) => await OpenDiffAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(openDiff);
        Grid.SetRow(actions, 1);
        grid.Children.Add(actions);
        AutomationProperties.SetName(changes, "Git working-tree changes");
        changes.DoubleTapped += async (_, _) =>
        {
            if (changes.SelectedItem is ChangeChoice choice)
            {
                await OpenFileAsync(choice.Change.Path, choice.GoalId);
            }
        };
        Grid.SetRow(changes, 2);
        grid.Children.Add(changes);
        Grid.SetRow(gitStatus, 3);
        grid.Children.Add(gitStatus);
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

    private Control BuildRunOutputTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,2*"),
            Margin = new Thickness(10),
            RowSpacing = 8,
        };
        Grid heading = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        AutomationProperties.SetName(runOutputStatus, "Durable run output status");
        runOutputStatus.Text = "Select a goal to inspect durable Build, Test, and Restore output.";
        heading.Children.Add(runOutputStatus);
        Button refresh = new() { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Refresh durable run output");
        refresh.Click += async (_, _) => await RefreshRunOutputAsync();
        Grid.SetColumn(refresh, 1);
        heading.Children.Add(refresh);
        grid.Children.Add(heading);

        AutomationProperties.SetName(runOutputs, "Durable Build, Test, and Restore runs");
        runOutputs.SelectionChanged += (_, _) => ShowSelectedRunOutput();
        Grid.SetRow(runOutputs, 1);
        grid.Children.Add(runOutputs);

        AutomationProperties.SetName(runOutputDetails, "Selected durable run output");
        Grid.SetRow(runOutputDetails, 2);
        grid.Children.Add(runOutputDetails);
        return grid;
    }

    private Control BuildProblemsTool()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*"),
            Margin = new Thickness(10),
            RowSpacing = 8,
        };
        Grid heading = new()
        {
            ColumnDefinitions = new("*,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            Children = { problemsStatus },
        };
        problemsStatus.Text = "Open a .NET source file to load compiler diagnostics.";
        AutomationProperties.SetName(problemsStatus, "Code intelligence status");
        AutomationProperties.SetName(showWarnings, "Show warning diagnostics");
        AutomationProperties.SetName(showInformation, "Show information diagnostics");
        AutomationProperties.SetName(showHidden, "Show hidden diagnostics");
        Grid.SetColumn(showWarnings, 1);
        Grid.SetColumn(showInformation, 2);
        Grid.SetColumn(showHidden, 3);
        heading.Children.Add(showWarnings);
        heading.Children.Add(showInformation);
        heading.Children.Add(showHidden);
        showWarnings.IsCheckedChanged += (_, _) => RenderProblems();
        showInformation.IsCheckedChanged += (_, _) => RenderProblems();
        showHidden.IsCheckedChanged += (_, _) => RenderProblems();
        grid.Children.Add(heading);
        AutomationProperties.SetName(problems, "Compiler and analyzer problems");
        problems.SelectionChanged += async (_, _) =>
        {
            if (problems.SelectedItem is ProblemChoice choice)
            {
                await NavigateToProblemAsync(choice);
            }
        };
        Grid.SetRow(problems, 1);
        grid.Children.Add(problems);
        return grid;
    }

    private void ScheduleDiagnostics(SourceDocumentSession session, bool immediate = false)
    {
        if (session.View.Sha256 is null || session.View.IsTruncated ||
            !IsDotNetSource(session.View.Path.Value))
        {
            session.Surface.SetCodeHealthNotApplicable();
            return;
        }

        session.Surface.BeginCodeHealthUpdate();
        if (session.Document.Id is { } documentId && documentDiagnostics.Remove(documentId))
        {
            RenderProblems();
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginDiagnostics(cancellationToken);
        _ = SynchronizeDiagnosticsAsync(session, version, token, immediate);
    }

    private async Task SynchronizeDiagnosticsAsync(
        SourceDocumentSession session,
        WorkbenchCodeBufferVersion version,
        CancellationToken requestCancellation,
        bool immediate)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), requestCancellation);
            }

            WorkbenchCodeSessionId? sessionId = await EnsureCodeSessionAsync(
                session,
                requestCancellation);
            if (sessionId is null || !session.IsCurrentDiagnostics(version))
            {
                return;
            }

            WorkbenchCodeDiagnosticView result = await codeIntelligenceService.SynchronizeAsync(
                new(
                    sessionId,
                    new(session.View.Path.Value),
                    new(session.View.Sha256!.Value),
                    version,
                    new(session.Editor.Text)),
                requestCancellation);
            if (!session.IsCurrentDiagnostics(version) ||
                result.State is WorkbenchCodeResultState.Stale or
                    WorkbenchCodeResultState.Cancelled)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (session.Document.Id is not { } documentId ||
                    !sourceDocuments.TryGetValue(documentId, out SourceDocumentSession? current) ||
                    !ReferenceEquals(current, session) || !session.IsCurrentDiagnostics(version))
                {
                    return;
                }

                session.Surface.UpdateCodeHealth(result);
                documentDiagnostics[documentId] = result;
                RenderProblems();
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                problemsStatus.Text = $"Code intelligence failed · {exception.Message}";
            });
        }
    }

    private async ValueTask<WorkbenchCodeSessionId?> EnsureCodeSessionAsync(
        SourceDocumentSession document,
        CancellationToken requestCancellation)
    {
        WorkspaceView? active = ActiveWorkspace();
        if (active is null || !active.IsTrusted || active.Id != document.View.WorkspaceId.Value)
        {
            return null;
        }

        string key = $"{active.Id}:{document.View.GoalId?.Value ?? "original"}:" +
                     $"{document.View.Branch?.Value ?? active.Branch}:{active.EntryPoint}";
        await codeSessionGate.WaitAsync(requestCancellation);
        try
        {
            if (codeSessionId is not null && string.Equals(codeSessionKey, key, StringComparison.Ordinal))
            {
                return codeSessionId;
            }

            if (codeSessionId is not null)
            {
                await codeIntelligenceService.StopAsync(codeSessionId, requestCancellation);
                codeSessionId = null;
                codeSessionKey = null;
            }

            string entryPoint = Path.IsPathRooted(active.EntryPoint)
                ? Path.GetRelativePath(active.RootPath, active.EntryPoint)
                : active.EntryPoint;
            if (entryPoint == ".." ||
                entryPoint.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    problemsStatus.Text = "Code intelligence unavailable · invalid workspace entry point.");
                return null;
            }

            IProgress<WorkbenchCodeLoadProgress> progress = new UiLoadProgress(problemsStatus);
            WorkbenchCodeSessionView started = await codeIntelligenceService.StartAsync(
                new(
                    new(active.Id),
                    document.View.GoalId,
                    new(entryPoint)),
                progress,
                requestCancellation);
            if (started.SessionId is null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    problemsStatus.Text = started.Issues.Count == 0
                        ? "Code intelligence unavailable."
                        : $"Code intelligence unavailable · {started.Issues[0].Message.Value}";
                });
                return null;
            }

            codeSessionId = started.SessionId;
            codeSessionKey = key;
            return started.SessionId;
        }
        finally
        {
            codeSessionGate.Release();
        }
    }

    private async ValueTask InvalidateCodeIntelligenceAsync()
    {
        try
        {
            await codeSessionGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (codeSessionId is not null)
            {
                await codeIntelligenceService.StopAsync(codeSessionId, cancellationToken);
            }

            codeSessionId = null;
            codeSessionKey = null;
            documentDiagnostics.Clear();
            problems.ItemsSource = Array.Empty<ProblemChoice>();
            problemsStatus.Text = "Open a .NET source file to load compiler diagnostics.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            codeSessionGate.Release();
        }
    }

    private void RenderProblems()
    {
        ProblemChoice[] choices = documentDiagnostics
            .SelectMany(pair => pair.Value.Diagnostics.Select(diagnostic => new ProblemChoice(
                diagnostic,
                sourceDocuments.TryGetValue(pair.Key, out SourceDocumentSession? session)
                    ? session.View.GoalId
                    : null)))
            .Where(choice => choice.Diagnostic.Severity switch
            {
                WorkbenchCodeDiagnosticSeverity.Error => true,
                WorkbenchCodeDiagnosticSeverity.Warning => showWarnings.IsChecked is true,
                WorkbenchCodeDiagnosticSeverity.Information => showInformation.IsChecked is true,
                WorkbenchCodeDiagnosticSeverity.Hidden => showHidden.IsChecked is true,
                _ => false,
            })
            .OrderByDescending(choice => choice.Diagnostic.Severity)
            .ThenBy(choice => choice.Diagnostic.Path.Value, StringComparer.Ordinal)
            .ThenBy(choice => choice.Diagnostic.Range.Start.Line)
            .Take(5_000)
            .ToArray();
        problems.ItemsSource = choices;
        int errors = choices.Count(choice =>
            choice.Diagnostic.Severity is WorkbenchCodeDiagnosticSeverity.Error);
        int warnings = choices.Count(choice =>
            choice.Diagnostic.Severity is WorkbenchCodeDiagnosticSeverity.Warning);
        WorkbenchCodeDiagnosticView? unavailable = documentDiagnostics.Values.FirstOrDefault(
            result => result.State is WorkbenchCodeResultState.Degraded or
                WorkbenchCodeResultState.Failed);
        problemsStatus.Text = unavailable?.Issues.FirstOrDefault() is { } issue
            ? $"Code intelligence {unavailable.State.ToString().ToLowerInvariant()} · " +
              issue.Message.Value
            : choices.Length == 0
                ? "No compiler or analyzer problems in the active buffers."
                : $"{errors:N0} error(s), {warnings:N0} warning(s), " +
                  $"{choices.Length - errors - warnings:N0} other finding(s).";
    }

    private async ValueTask NavigateToProblemAsync(ProblemChoice choice)
    {
        SourceDocumentSession? session = sourceDocuments.Values.FirstOrDefault(item =>
            item.View.GoalId == choice.GoalId &&
            item.View.Path.Value.Equals(choice.Diagnostic.Path.Value, StringComparison.Ordinal));
        if (session is null)
        {
            await OpenFileAsync(choice.Diagnostic.Path.Value, choice.GoalId);
            session = sourceDocuments.Values.FirstOrDefault(item =>
                item.View.GoalId == choice.GoalId &&
                item.View.Path.Value.Equals(choice.Diagnostic.Path.Value, StringComparison.Ordinal));
        }

        if (session is null)
        {
            return;
        }

        SetActiveDocument(session.Document);
        int line = choice.Diagnostic.Range.Start.Line + 1;
        int column = choice.Diagnostic.Range.Start.Character + 1;
        session.Editor.TextArea.Caret.Line = Math.Clamp(line, 1, session.Editor.Document.LineCount);
        session.Editor.TextArea.Caret.Column = Math.Max(1, column);
        session.Editor.ScrollTo(line, column);
        session.Editor.Focus();
    }

    private static bool IsDotNetSource(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    internal async ValueTask RefreshRunOutputAsync()
    {
        GoalView? goal = state().Goals.SelectedGoal;
        if (runOutputBusy)
        {
            return;
        }

        if (goal is null)
        {
            runOutputs.ItemsSource = Array.Empty<RunOutputChoice>();
            runOutputDetails.Text = string.Empty;
            runOutputStatus.Text = "Select a goal to inspect durable Build, Test, and Restore output.";
            return;
        }

        runOutputBusy = true;
        runOutputStatus.Text = $"Loading durable run output for {goal.Title}…";
        try
        {
            RunOutputSnapshot result = await runOutputService.ListAsync(goal.Id, cancellationToken);
            if (result.Error is not null)
            {
                runOutputs.ItemsSource = Array.Empty<RunOutputChoice>();
                runOutputDetails.Text = string.Empty;
                runOutputStatus.Text = result.Error;
                return;
            }

            RunOutputChoice[] choices = result.Items.Select(item => new RunOutputChoice(item)).ToArray();
            runOutputs.ItemsSource = choices;
            runOutputStatus.Text = choices.Length == 0
                ? $"No Build, Test, or Restore runs are recorded for {goal.Title}."
                : $"{choices.Length} durable run(s) for {goal.Title}." +
                  (result.IsTruncated ? " Showing the latest 200 runs." : string.Empty);
            runOutputs.SelectedIndex = choices.Length == 0 ? -1 : 0;
            if (choices.Length == 0)
            {
                runOutputDetails.Text = string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            runOutputStatus.Text = "Run-output refresh cancelled.";
        }
        catch (Exception exception)
        {
            runOutputs.ItemsSource = Array.Empty<RunOutputChoice>();
            runOutputDetails.Text = string.Empty;
            runOutputStatus.Text = $"Run output unavailable: {exception.Message}";
        }
        finally
        {
            runOutputBusy = false;
        }
    }

    private void ShowSelectedRunOutput()
    {
        runOutputDetails.Text = runOutputs.SelectedItem is RunOutputChoice choice
            ? FormatRunOutput(choice.Output)
            : string.Empty;
    }

    private static string FormatRunOutput(RunOutputView output)
    {
        List<string> lines =
        [
            $"{output.Operation} · {output.State}",
            $"Started: {output.StartedAt:O}",
            $"Completed: {(output.CompletedAt is null ? "not recorded" : output.CompletedAt.Value.ToString("O"))}",
            $"Correlation: {output.CorrelationId.Value}",
        ];
        if (output.Error is not null)
        {
            lines.Add($"Evidence error: {output.Error}");
            return string.Join(Environment.NewLine, lines);
        }

        if (output.Result is not { } result)
        {
            lines.Add(output.State is ToolEvidenceState.Running
                ? "The run is still active; output becomes available with durable completion evidence."
                : "No completed output was recorded for this run.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"Entry point: {result.EntryPoint}");
        lines.Add($"Exit code: {(result.ExitCode?.ToString() ?? "not reported")}");
        lines.Add($"Duration: {result.DurationMilliseconds:N0} ms");
        lines.Add($"Cancelled: {(result.WasCancelled ? "yes" : "no")}");
        if (result.Error is not null)
        {
            lines.Add($"Operation error: {result.Error}");
        }

        lines.Add(string.Empty);
        lines.Add(result.IsOutputTruncated ? "Standard output · truncated" : "Standard output");
        lines.Add(result.StandardOutput);
        lines.Add(string.Empty);
        lines.Add(result.IsErrorTruncated ? "Standard error · truncated" : "Standard error");
        lines.Add(result.StandardError);
        return string.Join(Environment.NewLine, lines);
    }

    private Control BuildOverviewDocument()
    {
        overviewAction.Classes.Add("primary");
        overviewAction.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(overviewAction, "Open or manage workspace");
        overviewAction.Click += async (_, _) => await manageWorkspace(ActiveWorkspace() is null);
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
                            overviewAction,
                        },
                    },
                },
            },
        };
    }

    private async ValueTask SearchAsync()
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted || string.IsNullOrWhiteSpace(query.Text))
        {
            fileStatus.Text = active is null
                ? "Select a workspace first."
                : active.IsTrusted ? "Enter text to search." : "Trust the workspace before searching files.";
            return;
        }

        await RunAsync(async () =>
        {
            WorkbenchTextSearchResult inspected = await inspectionService.SearchTextAsync(
                WorkbenchRequest(active),
                query.Text.Trim(),
                cancellationToken);
            WorkspaceTextSearchView result = inspected.Search;
            searchResults.ItemsSource = result.Matches
                .Select(match => new SearchChoice(match, inspected.Context.GoalId))
                .ToArray();
            searchResults.IsVisible = result.Matches.Count > 0;
            fileStatus.Text = result.Error ??
                              $"{inspected.Context.Description} · {result.Matches.Count} match(es) " +
                              $"in {result.FilesScanned} file(s)" +
                              (result.IsTruncated ? " · truncated." : ".");
        });
    }

    private SourceDocumentSession CreateSourceDocument(
        string id,
        WorkbenchDocumentView view)
    {
        SourceEditorSurface surface = SourceEditorSurface.Create(view);
        TextEditor editor = surface.Editor;
        AutomationProperties.SetName(
            editor,
            view.Access is WorkbenchDocumentAccess.Editable
                ? $"Editable source editor for {view.Path.Value}"
                : $"Read-only source editor for {view.Path.Value}");

        SourceDockDocument document = new()
        {
            Id = id,
            Title = SourceDocumentTitle(view),
            Factory = factory,
            CanClose = true,
            CanFloat = true,
        };
        WorkbenchDockContent.Attach(document, surface.Control);
        SourceDocumentSession session = new(
            document,
            surface,
            view);
        document.CloseRequested = () => OnSourceDocumentCloseRequested(session);
        editor.TextChanged += (_, _) =>
        {
            session.CancelHover();
            session.SynchronizeDirtyState();
            ScheduleDiagnostics(session);
        };
        editor.KeyDown += async (_, args) =>
        {
            if (args.Key is Key.Space && args.KeyModifiers == KeyModifiers.Control)
            {
                args.Handled = true;
                await ShowCompletionAsync(
                    session,
                    WorkbenchCodeCompletionTriggerKind.Invoke,
                    triggerCharacter: null);
            }
            else if (args.Key is Key.K && args.KeyModifiers == KeyModifiers.Control)
            {
                args.Handled = true;
                await ShowQuickInfoAsync(session);
            }
            else if (args.Key is Key.F12 && args.KeyModifiers == KeyModifiers.None)
            {
                args.Handled = true;
                await NavigateSymbolAsync(session, SemanticNavigationKind.Definition);
            }
            else if (args.Key is Key.F12 && args.KeyModifiers == KeyModifiers.Shift)
            {
                args.Handled = true;
                await NavigateSymbolAsync(session, SemanticNavigationKind.References);
            }
            else if (args.Key is Key.F12 && args.KeyModifiers == KeyModifiers.Control)
            {
                args.Handled = true;
                await NavigateSymbolAsync(session, SemanticNavigationKind.Implementations);
            }
            else if (args.Key is Key.F7 && args.KeyModifiers == KeyModifiers.Alt)
            {
                args.Handled = true;
                await NavigateSymbolAsync(session, SemanticNavigationKind.References);
            }
            else if (args.Key is Key.B &&
                     args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
            {
                args.Handled = true;
                await NavigateSymbolAsync(session, SemanticNavigationKind.Implementations);
            }
            else if (args.Key is Key.F2 && args.KeyModifiers == KeyModifiers.None)
            {
                args.Handled = true;
                await RenameSymbolAsync(session);
            }
            else if (args.Key is Key.S && args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                args.Handled = true;
                await SaveSourceDocumentAsync(session);
            }
            else if (args.Key is Key.W && args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                args.Handled = true;
                await RequestSourceDocumentCloseAsync(session);
            }
        };
        editor.TextArea.TextEntered += async (_, args) =>
        {
            if (args.Text is not { Length: 1 })
            {
                return;
            }

            char value = args.Text[0];
            if (value is '(' or ',')
            {
                await ShowSignatureHelpAsync(session);
            }
            else if (value == ')')
            {
                session.SignatureWindow?.Hide();
                session.SignatureWindow = null;
            }

            if (char.IsLetterOrDigit(value) || value is '_' or '.')
            {
                await ShowCompletionAsync(
                    session,
                    WorkbenchCodeCompletionTriggerKind.Insertion,
                    value);
            }
        };
        editor.PointerMoved += (_, args) =>
        {
            var position = editor.GetPositionFromPoint(args.GetPosition(editor));
            if (position is { } value)
            {
                _ = ShowQuickInfoOnHoverAsync(
                    session,
                    new(value.Line - 1, value.Column - 1),
                    session.BeginHover(cancellationToken));
            }
        };
        editor.PointerExited += (_, _) => session.CancelHover();
        surface.Save.Click += async (_, _) => await SaveSourceDocumentAsync(session);
        surface.Reload.Click += async (_, _) => await ReloadSourceDocumentAsync(session, confirmDiscard: true);
        surface.Close.Click += async (_, _) => await RequestSourceDocumentCloseAsync(session);
        surface.Completion.Click += async (_, _) => await ShowCompletionAsync(
            session, WorkbenchCodeCompletionTriggerKind.Invoke, triggerCharacter: null);
        surface.SymbolInfo.Click += async (_, _) => await ShowQuickInfoAsync(session);
        surface.Definition.Click += async (_, _) => await NavigateSymbolAsync(
            session, SemanticNavigationKind.Definition);
        surface.References.Click += async (_, _) => await NavigateSymbolAsync(
            session, SemanticNavigationKind.References);
        surface.Implementations.Click += async (_, _) => await NavigateSymbolAsync(
            session, SemanticNavigationKind.Implementations);
        sourceDocuments.Add(id, session);
        session.SynchronizeDirtyState();
        ScheduleDiagnostics(session, immediate: true);
        return session;
    }

    private async ValueTask ShowCompletionAsync(
        SourceDocumentSession session,
        WorkbenchCodeCompletionTriggerKind triggerKind,
        char? triggerCharacter)
    {
        if (!CanUseSemanticAssistance(session))
        {
            return;
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await EnsureCodeSessionAsync(session, token);
            if (codeSession is null || !session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = InteractiveSnapshot(
                session, codeSession, version);
            WorkbenchCodeCompletionView result = await codeIntelligenceService.GetCompletionsAsync(
                new(snapshot, triggerKind, triggerCharacter), token);
            if (!session.IsCurrentInteraction(version) || result.ListId is null ||
                result.Items.Count == 0 || result.State is WorkbenchCodeResultState.Stale or
                    WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Failed)
            {
                return;
            }

            session.CompletionWindow?.Hide();
            CompletionWindow window = new RoslynCompletionWindow(session.Editor.TextArea)
            {
                StartOffset = Offset(session.Editor, result.ApplicableRange.Start),
                EndOffset = Offset(session.Editor, result.ApplicableRange.End),
                CloseWhenCaretAtBeginning = triggerKind is WorkbenchCodeCompletionTriggerKind.Invoke,
            };
            foreach (WorkbenchCodeCompletionItem item in result.Items)
            {
                window.CompletionList.CompletionData.Add(new RoslynCompletionData(
                    item,
                    (selected, commitCharacter) =>
                        _ = CommitCompletionAsync(
                            session,
                            snapshot,
                            result.ListId,
                            selected,
                            commitCharacter)));
            }

            AutomationProperties.SetName(
                window.CompletionList,
                $"Code completions for {session.View.Path.Value}");
            session.CompletionWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async ValueTask RenameSymbolAsync(SourceDocumentSession session)
    {
        if (mutationService is null || session.View.GoalId is null ||
            session.View.Access is not WorkbenchDocumentAccess.Editable ||
            !CanUseSemanticAssistance(session) || OwnerWindow() is not { } owner)
        {
            session.SetStatus("Semantic rename requires an editable approved goal source document.");
            return;
        }

        RenameNameDialog name = new();
        await name.ShowDialog(owner);
        if (name.Result is not { } newName)
        {
            return;
        }

        PendingWorkbenchRename? pending = await PreviewActiveRenameAsync(newName);
        if (pending is null)
        {
            return;
        }

        RenamePreviewDialog preview = new(pending.Preview);
        if (!await preview.ShowDialog<bool>(owner))
        {
            session.SetStatus("Rename preview closed without changing files.");
            return;
        }

        _ = await ApplyActiveRenameAsync(pending);
    }

    internal async ValueTask<PendingWorkbenchRename?> PreviewActiveRenameAsync(string newName)
    {
        if (mutationService is null || activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session) ||
            session.View.GoalId is null || session.View.Sha256 is null ||
            session.View.Access is not WorkbenchDocumentAccess.Editable ||
            session.View.IsTruncated)
        {
            return null;
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        RenameSymbolPreviewRequest request = new(
            session.View.GoalId.Value,
            new(session.View.Path.Value),
            new(session.View.Sha256.Value),
            version,
            new(session.Editor.Text),
            new(
                session.Editor.TextArea.Caret.Line - 1,
                session.Editor.TextArea.Caret.Column - 1),
            new(newName),
            RenameSymbolOrigin.Human,
            []);
        session.SetBusy(true, "Resolving rename with Roslyn…");
        try
        {
            RenameSymbolPreviewView result = await mutationService.PreviewRenameAsync(request, token);
            if (result.Preview is null || result.ErrorCode is not null)
            {
                session.SetStatus(result.Error ?? "Rename preview is unavailable.");
                return null;
            }

            if (result.Preview.Disposition is not WorkbenchCodeTransformationDisposition.Ready ||
                result.Preview.Fingerprint is null)
            {
                session.SetStatus(result.Preview.Conflicts.FirstOrDefault()?.Message.Value ??
                    result.Preview.Issues.FirstOrDefault()?.Message.Value ??
                    "Rename has conflicts and cannot be applied.");
            }
            else
            {
                session.SetStatus(
                    $"Rename preview ready · {result.Preview.Edits.Count} affected file(s).");
            }

            return new(request, result.Preview);
        }
        catch (OperationCanceledException)
        {
            session.SetStatus("Rename preview cancelled.");
            return null;
        }
        finally
        {
            session.SetBusy(false);
            await InvalidateCodeIntelligenceAsync();
        }
    }

    internal async ValueTask<RenameSymbolApplyView?> ApplyActiveRenameAsync(
        PendingWorkbenchRename pending)
    {
        if (mutationService is null || pending.Preview.Fingerprint is null ||
            activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? active))
        {
            return null;
        }

        active.SetBusy(true, "Applying the accepted rename atomically…");
        try
        {
            RenameSymbolApplyView result = await mutationService.ApplyRenameAsync(new(
                pending.Request,
                NewEditCorrelation(),
                pending.Preview.Fingerprint), cancellationToken);
            if (result.ErrorCode is not null)
            {
                active.SetStatus(result.Error ?? "Rename was not applied.");
                return result;
            }

            foreach (WorkbenchCodeRenameEdit edit in result.Preview!.Edits)
            {
                SourceDocumentSession? open = sourceDocuments.Values.FirstOrDefault(candidate =>
                    candidate.View.Path.Value.Equals(edit.Path.Value, StringComparison.Ordinal));
                FileEditView? evidence = result.Files.FirstOrDefault(file =>
                    file.Path.Equals(edit.Path.Value, StringComparison.Ordinal));
                if (open is null || evidence?.NewSha256 is null)
                {
                    continue;
                }

                open.ReplaceWith(open.View with
                {
                    Content = new(edit.Text.Value),
                    Sha256 = new(evidence.NewSha256),
                    Size = new(evidence.BytesWritten),
                });
                open.SetStatus(
                    $"Renamed to {pending.Preview.NewName.Value} · compiler-verified atomic apply.");
            }

            await InvalidateCodeIntelligenceAsync();
            ScheduleDiagnostics(active, immediate: true);
            return result;
        }
        catch (OperationCanceledException)
        {
            active.SetStatus("Rename cancelled; no partial file set was accepted.");
            return null;
        }
        finally
        {
            active.SetBusy(false);
        }
    }

    private async Task CommitCompletionAsync(
        SourceDocumentSession session,
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeCompletionListId listId,
        WorkbenchCodeCompletionItem item,
        char? commitCharacter)
    {
        try
        {
            WorkbenchCodeCompletionCommitView result =
                await codeIntelligenceService.CommitCompletionAsync(
                    new(snapshot, listId, item.Id, commitCharacter),
                    cancellationToken);
            if (!session.IsCurrentInteraction(snapshot.BufferVersion) ||
                result.State is WorkbenchCodeResultState.Stale or
                    WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Failed)
            {
                session.SetStatus("Completion expired because the document changed.");
                return;
            }

            foreach (WorkbenchCodeTextChange change in result.Changes
                         .OrderByDescending(value => Offset(session.Editor, value.Range.Start)))
            {
                int start = Offset(session.Editor, change.Range.Start);
                int end = Offset(session.Editor, change.Range.End);
                session.Editor.Document.Replace(start, Math.Max(0, end - start), change.Text.Value);
            }

            if (result.NewPosition is { } position)
            {
                session.Editor.TextArea.Caret.Offset = Offset(session.Editor, position);
            }
            else if (result.Changes.LastOrDefault() is { } last)
            {
                session.Editor.TextArea.Caret.Offset =
                    Offset(session.Editor, last.Range.Start) + last.Text.Value.Length;
            }

            if (commitCharacter is { } value && value is not '\t' and not '\n' &&
                (session.Editor.TextArea.Caret.Offset >= session.Editor.Document.TextLength ||
                 session.Editor.Document.GetCharAt(session.Editor.TextArea.Caret.Offset) != value))
            {
                session.Editor.Document.Insert(session.Editor.TextArea.Caret.Offset, value.ToString());
                session.Editor.TextArea.Caret.Offset++;
            }

            session.SetStatus($"Completed {item.DisplayText.Value} with Roslyn.");
            session.Editor.Focus();
            if (commitCharacter == '(')
            {
                await ShowSignatureHelpAsync(session);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ShowQuickInfoOnHoverAsync(
        SourceDocumentSession session,
        WorkbenchCodePosition position,
        CancellationToken hoverToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), hoverToken);
            if (session.CompletionWindow?.IsVisible is true)
            {
                return;
            }
            await ShowQuickInfoAsync(session, position);
        }
        catch (OperationCanceledException) when (hoverToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask ShowQuickInfoAsync(
        SourceDocumentSession session,
        WorkbenchCodePosition? requestedPosition = null)
    {
        if (!CanUseSemanticAssistance(session))
        {
            return;
        }

        session.CompletionWindow?.Hide();
        session.CompletionWindow = null;

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await EnsureCodeSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            WorkbenchCodeQuickInfoView result = await codeIntelligenceService.GetQuickInfoAsync(
                InteractiveSnapshot(session, codeSession, version, requestedPosition), token);
            if (!session.IsCurrentInteraction(version) || result.Sections.Count == 0)
            {
                session.SetStatus("No symbol information is available at the caret.");
                return;
            }

            session.QuickInfoWindow?.Hide();
            StackPanel content = new() { Spacing = 6, MaxWidth = 760 };
            foreach (WorkbenchCodeMessage section in result.Sections)
            {
                content.Children.Add(new TextBlock
                {
                    Text = section.Value,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
                });
            }

            Border card = new() { Child = content, Padding = new(10) };
            card.Classes.Add("semantic-insight");
            AutomationProperties.SetName(card,
                $"Quick info for {session.View.Path.Value}: " +
                string.Join(" ", result.Sections.Select(section => section.Value)));
            InsightWindow window = new(session.Editor.TextArea)
            {
                Child = card,
                StartOffset = result.ApplicableRange is null
                    ? session.Editor.TextArea.Caret.Offset
                    : Offset(session.Editor, result.ApplicableRange.Start),
                EndOffset = result.ApplicableRange is null
                    ? session.Editor.TextArea.Caret.Offset
                    : Offset(session.Editor, result.ApplicableRange.End),
            };
            session.QuickInfoWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async ValueTask ShowSignatureHelpAsync(SourceDocumentSession session)
    {
        if (!CanUseSemanticAssistance(session))
        {
            return;
        }

        session.CompletionWindow?.Hide();
        session.CompletionWindow = null;

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await EnsureCodeSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            WorkbenchCodeSignatureHelpView result =
                await codeIntelligenceService.GetSignatureHelpAsync(
                    InteractiveSnapshot(session, codeSession, version), token);
            if (!session.IsCurrentInteraction(version) || result.Signatures.Count == 0)
            {
                return;
            }

            session.SignatureWindow?.Hide();
            OverloadInsightWindow window = new(session.Editor.TextArea)
            {
                Provider = new RoslynOverloadProvider(result),
                StartOffset = Math.Max(0, session.Editor.TextArea.Caret.Offset - 1),
                EndOffset = session.Editor.Document.TextLength,
            };
            session.SignatureWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async ValueTask NavigateSymbolAsync(
        SourceDocumentSession session,
        SemanticNavigationKind kind)
    {
        if (!CanUseSemanticAssistance(session))
        {
            return;
        }

        session.CloseInteractiveWindows();

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await EnsureCodeSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = InteractiveSnapshot(
                session, codeSession, version);
            session.SetStatus(kind switch
            {
                SemanticNavigationKind.Definition => "Finding definition with Roslyn…",
                SemanticNavigationKind.References => "Finding usages with Roslyn…",
                SemanticNavigationKind.Implementations => "Finding implementations with Roslyn…",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            });
            WorkbenchCodeNavigationView result = kind switch
            {
                SemanticNavigationKind.Definition =>
                    await codeIntelligenceService.FindDefinitionAsync(snapshot, token),
                SemanticNavigationKind.References =>
                    await codeIntelligenceService.FindReferencesAsync(snapshot, token),
                SemanticNavigationKind.Implementations =>
                    await codeIntelligenceService.FindImplementationsAsync(snapshot, token),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            if (!session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeSymbolDestination[] source = result.Destinations
                .Where(destination => destination.Kind is WorkbenchCodeDestinationKind.Source &&
                    destination.Path is not null && destination.Range is not null)
                .ToArray();
            if (kind is not SemanticNavigationKind.References && source.Length == 1)
            {
                await NavigateToSymbolAsync(source[0], session.View.GoalId);
                return;
            }

            if (source.Length == 0)
            {
                session.SetStatus(result.Destinations.FirstOrDefault()?.Display.Value ??
                    "No editable source destination is available for this symbol.");
                return;
            }

            session.SetStatus($"Found {source.Length:N0} source {NavigationLabel(kind)} " +
                              (source.Length == 1 ? "destination." : "destinations."));

            ListBox list = new()
            {
                ItemsSource = source.Select(destination => new SymbolDestinationChoice(destination))
                    .ToArray(),
                MaxHeight = 320,
                MinWidth = 420,
            };
            AutomationProperties.SetName(list,
                $"{source.Length} source {NavigationLabel(kind)} destinations for " +
                session.View.Path.Value);
            InsightWindow window = new(session.Editor.TextArea)
            {
                Child = list,
                StartOffset = session.Editor.TextArea.Caret.Offset,
                EndOffset = session.Editor.TextArea.Caret.Offset,
            };
            list.SelectionChanged += async (_, _) =>
            {
                if (list.SelectedItem is SymbolDestinationChoice choice)
                {
                    window.Hide();
                    await NavigateToSymbolAsync(choice.Destination, session.View.GoalId);
                }
            };
            session.QuickInfoWindow?.Hide();
            session.QuickInfoWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static string NavigationLabel(SemanticNavigationKind kind) => kind switch
    {
        SemanticNavigationKind.Definition => "definition",
        SemanticNavigationKind.References => "usage",
        SemanticNavigationKind.Implementations => "implementation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private enum SemanticNavigationKind
    {
        Definition,
        References,
        Implementations,
    }

    private async ValueTask NavigateToSymbolAsync(
        WorkbenchCodeSymbolDestination destination,
        GoalId? goalId)
    {
        if (destination.Path is null || destination.Range is null)
        {
            return;
        }

        await OpenFileAsync(destination.Path.Value, goalId);
        SourceDocumentSession? target = sourceDocuments.Values.FirstOrDefault(value =>
            value.View.GoalId == goalId &&
            value.View.Path.Value.Equals(destination.Path.Value, StringComparison.Ordinal));
        if (target is null)
        {
            return;
        }

        SetActiveDocument(target.Document);
        int line = destination.Range.Start.Line + 1;
        int column = destination.Range.Start.Character + 1;
        target.Editor.TextArea.Caret.Line = Math.Clamp(line, 1, target.Editor.Document.LineCount);
        target.Editor.TextArea.Caret.Column = Math.Max(1, column);
        target.Editor.ScrollTo(line, column);
        target.Editor.Focus();
    }

    private static WorkbenchCodeInteractiveSnapshot InteractiveSnapshot(
        SourceDocumentSession session,
        WorkbenchCodeSessionId codeSession,
        WorkbenchCodeBufferVersion version,
        WorkbenchCodePosition? requestedPosition = null) => new(
        codeSession,
        new(session.View.Path.Value),
        new(session.View.Sha256!.Value),
        version,
        new(session.Editor.Text),
        requestedPosition ?? new(
            session.Editor.TextArea.Caret.Line - 1,
            session.Editor.TextArea.Caret.Column - 1));

    private static bool CanUseSemanticAssistance(SourceDocumentSession session) =>
        session.View.Sha256 is not null && !session.View.IsTruncated &&
        IsDotNetSource(session.View.Path.Value);

    private static int Offset(TextEditor editor, WorkbenchCodePosition position)
    {
        int line = Math.Clamp(position.Line + 1, 1, editor.Document.LineCount);
        var documentLine = editor.Document.GetLineByNumber(line);
        int character = Math.Clamp(position.Character, 0, documentLine.Length);
        return documentLine.Offset + character;
    }

    private async ValueTask<bool> SaveSourceDocumentAsync(
        SourceDocumentSession session,
        WorkbenchDocumentSha256? overrideBaseline = null)
    {
        if (session.View.Access is not WorkbenchDocumentAccess.Editable || !session.IsDirty)
        {
            return !session.IsDirty;
        }

        session.SetBusy(
            true,
            session.View.GoalId is null
                ? "Saving to the active trusted workspace…"
                : "Saving through the approved goal worktree…");
        try
        {
            WorkbenchDocumentSha256? baseline = overrideBaseline ?? session.View.Sha256;
            while (true)
            {
                WorkbenchDocumentSaveResult result = await documentService.SaveAsync(
                    new(
                        session.View.WorkspaceId,
                        session.View.GoalId,
                        NewEditCorrelation(),
                        session.View.Path,
                        baseline,
                        new(session.Editor.Text)),
                    cancellationToken);
                if (result.Outcome is WorkbenchDocumentSaveOutcome.Saved &&
                    result.SavedSha256 is not null)
                {
                    session.AcceptSaved(result.SavedSha256, result.BytesWritten);
                    ScheduleDiagnostics(session, immediate: true);
                    return true;
                }

                if (result.Outcome is WorkbenchDocumentSaveOutcome.Conflict)
                {
                    session.SetStatus(
                        result.CurrentSha256 is null
                            ? "Save conflict: the file was deleted in the goal worktree."
                            : "Save conflict: the file changed in the goal worktree.");
                    WorkbenchConflictDecision decision = await documentPrompt.DecideConflictAsync(
                        new(session.View.Path.Value, result.CurrentSha256 is null),
                        OwnerWindow());
                    if (decision is WorkbenchConflictDecision.Reload)
                    {
                        return await ReloadSourceDocumentAsync(session, confirmDiscard: false);
                    }

                    if (decision is WorkbenchConflictDecision.Overwrite)
                    {
                        baseline = result.CurrentSha256;
                        continue;
                    }

                    if (decision is not WorkbenchConflictDecision.Cancel)
                    {
                        throw new ArgumentOutOfRangeException(nameof(decision));
                    }

                    return false;
                }

                session.SetStatus(result.Error ?? "The source document was not saved.");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            session.SetStatus("Source save cancelled; editor changes are still present.");
            return false;
        }
        catch (Exception exception)
        {
            session.SetStatus($"Source save failed: {exception.Message}");
            return false;
        }
        finally
        {
            session.SetBusy(false);
        }
    }

    private async ValueTask<bool> ReloadSourceDocumentAsync(
        SourceDocumentSession session,
        bool confirmDiscard)
    {
        if (confirmDiscard && session.IsDirty)
        {
            WorkbenchUnsavedDecision decision = await documentPrompt.DecideUnsavedAsync(
                new(session.View.Path.Value, WorkbenchDocumentTransition.Reload),
                OwnerWindow());
            if (decision is WorkbenchUnsavedDecision.Cancel)
            {
                return false;
            }

            if (decision is WorkbenchUnsavedDecision.Save)
            {
                return await SaveSourceDocumentAsync(session);
            }
        }

        session.SetBusy(true, "Reloading from the workspace…");
        try
        {
            WorkbenchDocumentView current = await documentService.OpenAsync(
                new(session.View.WorkspaceId, session.View.GoalId, session.View.Path),
                cancellationToken);
            if (current.ErrorCode == "file_missing")
            {
                session.AllowClose = true;
                factory.CloseDockable(session.Document);
                fileStatus.Text = $"{session.View.Path.Value} no longer exists; the stale document was closed.";
                return true;
            }

            if (current.Error is not null)
            {
                session.SetStatus($"Reload failed: {current.Error}");
                return false;
            }

            session.ReplaceWith(current);
            return true;
        }
        catch (OperationCanceledException)
        {
            session.SetStatus("Reload cancelled; editor content was kept.");
            return false;
        }
        catch (Exception exception)
        {
            session.SetStatus($"Reload failed: {exception.Message}");
            return false;
        }
        finally
        {
            session.SetBusy(false);
        }
    }

    private async ValueTask RequestSourceDocumentCloseAsync(SourceDocumentSession session)
    {
        if (!session.IsDirty || await ResolveUnsavedAsync(
                session,
                WorkbenchDocumentTransition.Close,
                discardKeepsDocument: false))
        {
            session.AllowClose = true;
            factory.CloseDockable(session.Document);
        }
    }

    private bool OnSourceDocumentCloseRequested(SourceDocumentSession session)
    {
        if (!session.IsDirty || session.AllowClose)
        {
            return true;
        }

        if (resolvingDocumentTransition)
        {
            return false;
        }

        resolvingDocumentTransition = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RequestSourceDocumentCloseAsync(session);
                if (sourceDocuments.ContainsKey(session.Document.Id ?? string.Empty))
                {
                    session.IgnoreNextActivationChange = true;
                    SetActiveDocument(session.Document);
                }
            }
            finally
            {
                resolvingDocumentTransition = false;
            }
        });
        return false;
    }

    private async ValueTask<bool> ResolveUnsavedAsync(
        SourceDocumentSession session,
        WorkbenchDocumentTransition transition,
        bool discardKeepsDocument)
    {
        WorkbenchUnsavedDecision decision = await documentPrompt.DecideUnsavedAsync(
            new(session.View.Path.Value, transition),
            OwnerWindow());
        switch (decision)
        {
            case WorkbenchUnsavedDecision.Save:
                return await SaveSourceDocumentAsync(session);
            case WorkbenchUnsavedDecision.Discard:
                if (discardKeepsDocument)
                {
                    session.DiscardChanges();
                }

                return true;
            case WorkbenchUnsavedDecision.Cancel:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision));
        }
    }

    private async ValueTask<bool> PrepareActiveDocumentTransitionAsync(
        WorkbenchDocumentTransition transition)
    {
        if (activeDocument?.Id is null ||
            !sourceDocuments.TryGetValue(activeDocument.Id, out SourceDocumentSession? session) ||
            !session.IsDirty)
        {
            return true;
        }

        return await ResolveUnsavedAsync(
            session,
            transition,
            discardKeepsDocument: true);
    }

    private async ValueTask<bool> TrySwitchDocumentAsync(IDockable next)
    {
        if (ReferenceEquals(activeDocument, next))
        {
            return true;
        }

        if (!await PrepareActiveDocumentTransitionAsync(WorkbenchDocumentTransition.Switch))
        {
            return false;
        }

        SetActiveDocument(next);
        return true;
    }

    private async void OnActiveDockableChanged(
        object? sender,
        Dock.Model.Core.Events.ActiveDockableChangedEventArgs args)
    {
        IDockable? next = args.Dockable;
        if (next is null || suppressDocumentActivation || resolvingDocumentTransition ||
            !IsDocument(next) || ReferenceEquals(activeDocument, next))
        {
            return;
        }

        IDockable? previous = activeDocument;
        if (previous?.Id is not null &&
            sourceDocuments.TryGetValue(previous.Id, out SourceDocumentSession? pending) &&
            pending.IgnoreNextActivationChange)
        {
            pending.IgnoreNextActivationChange = false;
            SetActiveDocument(previous);
            return;
        }

        if (previous?.Id is null ||
            !sourceDocuments.TryGetValue(previous.Id, out SourceDocumentSession? session) ||
            !session.IsDirty || session.AllowClose)
        {
            activeDocument = next;
            UpdateDocumentSwitcher();
            return;
        }

        resolvingDocumentTransition = true;
        try
        {
            SetActiveDocument(previous);
            if (await ResolveUnsavedAsync(
                    session,
                    WorkbenchDocumentTransition.Switch,
                    discardKeepsDocument: true))
            {
                SetActiveDocument(next);
            }
        }
        finally
        {
            resolvingDocumentTransition = false;
        }
    }

    private void OnDockableClosed(
        object? sender,
        Dock.Model.Core.Events.DockableClosedEventArgs args)
    {
        IDockable? dockable = args.Dockable;
        if (dockable?.Id is { } id && sourceDocuments.Remove(id, out SourceDocumentSession? session))
        {
            documentDiagnostics.Remove(id);
            session.Dispose();
            RenderProblems();
        }

        if (ReferenceEquals(activeDocument, dockable))
        {
            activeDocument = overviewDocument;
        }

        Dispatcher.UIThread.Post(UpdateDocumentSwitcher);
    }

    private void SetActiveDocument(IDockable document)
    {
        suppressDocumentActivation = true;
        try
        {
            factory.SetActiveDockable(document);
            activeDocument = document;
            UpdateDocumentSwitcher();
        }
        finally
        {
            suppressDocumentActivation = false;
        }
    }

    private static bool IsDocument(IDockable dockable) =>
        dockable is IDocument && dockable is not ITool;

    private Window? OwnerWindow() => TopLevel.GetTopLevel(Control) as Window;

    private static ToolCorrelationId NewEditCorrelation() =>
        new($"desktop-edit-{Guid.NewGuid():N}");

    private static string SourceDocumentId(WorkbenchDocumentView view) =>
        $"document.file.{view.WorkspaceId.Value}.{view.GoalId?.Value ?? "original"}.{view.Path.Value}";

    private static string SourceDocumentTitle(WorkbenchDocumentView view)
    {
        string title = Path.GetFileName(view.Path.Value);
        if (view.IsTruncated)
        {
            return $"{title} · truncated";
        }

        return view.Branch is null ? title : $"{title} · {view.Branch.Value}";
    }

    private IDockable OpenOrReplaceDocument(string id, string title, Control content)
    {
        IDockable? existing = documents.VisibleDockables?.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Title = title;
            WorkbenchDockContent.Attach(existing, content);
            SetActiveDocument(existing);
            return existing;
        }

        factory.Document(out IDocument? document, item => item
            .WithId(id)
            .WithTitle(title)
            .WithCanClose(true)
            .WithCanFloat(true)
            .WithContext(content));
        IDocument created = document ?? throw new InvalidOperationException("Dock did not create the document.");
        WorkbenchDockContent.Attach(created, content);
        documents.AddDocument(created);
        SetActiveDocument(created);
        return created;
    }

    private static Control CreateDiffView(string diff)
    {
        Control view = DiffContentView.Create(diff);
        AutomationProperties.SetName(view, "Git working-tree diff");
        return view;
    }

    private static Control CreateEditor(string content, string path, bool showLineNumbers)
    {
        TextEditor editor = CodeEditorView.Create(
            content,
            isReadOnly: true,
            wordWrap: false,
            showLineNumbers: showLineNumbers,
            path: path);
        AutomationProperties.SetName(editor, $"Read-only editor for {path}");
        return editor;
    }

    private async ValueTask<bool> CloseAllSourceDocumentsAsync(
        WorkbenchDocumentTransition transition)
    {
        foreach (SourceDocumentSession session in sourceDocuments.Values.ToArray())
        {
            if (session.IsDirty && !await ResolveUnsavedAsync(
                    session,
                    transition,
                    discardKeepsDocument: false))
            {
                SetActiveDocument(session.Document);
                return false;
            }

            session.AllowClose = true;
            factory.CloseDockable(session.Document);
        }

        foreach (IDockable document in documents.VisibleDockables?
                     .Where(item => !string.Equals(
                         item.Id,
                         WorkbenchDockIds.OverviewDocument,
                         StringComparison.Ordinal))
                     .ToArray() ?? [])
        {
            factory.CloseDockable(document);
        }

        SetActiveDocument(overviewDocument);
        return true;
    }

    private void ActivateOverview() => SetActiveDocument(overviewDocument);

    private sealed record DocumentChoice(IDockable Document)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Document.Title) ? "Untitled document" : Document.Title;
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
        if (args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) &&
            args.Key is Key.E)
        {
            args.Handled = ShowFiles();
        }
        else if (args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) &&
                 args.Key is Key.G)
        {
            args.Handled = ShowGit();
        }
        else if (args.KeyModifiers == KeyModifiers.Control && args.Key is Key.J)
        {
            args.Handled = ShowRunOutput();
        }
        else if (args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) &&
                 args.Key is Key.M)
        {
            args.Handled = ShowProblems();
        }
        else if (args.Key is Key.F6 && args.KeyModifiers is KeyModifiers.None)
        {
            args.Handled = FocusNextRegion();
        }
    }

    /// <summary>Activates the Files panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowFiles() => ActivateTool(WorkbenchDockIds.FilesTool);

    /// <summary>Restores and activates the primary Conversation panel.</summary>
    internal bool ShowConversation() => ActivateTool(WorkbenchDockIds.ConversationTool);

    /// <summary>Activates the Git panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowGit() => ActivateTool(WorkbenchDockIds.GitTool);

    internal string GitStatusText => gitStatus.Text ?? string.Empty;

    internal string GitSummaryText => gitSummary.Text ?? string.Empty;

    /// <summary>Activates the Run output panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowRunOutput() => ActivateTool(WorkbenchDockIds.RunOutputTool);

    /// <summary>Activates the Problems panel, the same path as its keyboard shortcut.</summary>
    internal bool ShowProblems() => ActivateTool(WorkbenchDockIds.ProblemsTool);

    private bool ActivateTool(string id)
    {
        IDockable? tool = FindDockable(root, id);
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
        if (!visibleInOwner && DefaultToolDock(id) is { } defaultOwner)
        {
            RemoveFromRootSpecialCollections(tool);
            factory.AddDockable(defaultOwner, tool);
        }

        if (tool.Owner is IToolDock owner)
        {
            RestoreAdaptiveProportion(owner);
            owner.IsExpanded = true;
        }

        factory.SetActiveDockable(tool);
        FocusContext(tool);
        return true;
    }

    private IToolDock? DefaultToolDock(string id) => id switch
    {
        WorkbenchDockIds.NavigationTool or WorkbenchDockIds.FilesTool => leftTools,
        WorkbenchDockIds.ContextTool or WorkbenchDockIds.GitTool => rightTools,
        WorkbenchDockIds.ConversationTool or WorkbenchDockIds.RunOutputTool or
            WorkbenchDockIds.ProblemsTool => bottomTools,
        _ => null,
    };

    private void RemoveFromRootSpecialCollections(IDockable tool)
    {
        root.HiddenDockables?.Remove(tool);
        root.LeftPinnedDockables?.Remove(tool);
        root.RightPinnedDockables?.Remove(tool);
        root.TopPinnedDockables?.Remove(tool);
        root.BottomPinnedDockables?.Remove(tool);
    }

    private void RestoreAdaptiveProportion(IToolDock owner)
    {
        if (ReferenceEquals(owner, leftTools) && adaptiveLeftCollapsed)
        {
            owner.Proportion = expandedLeftProportion;
            owner.CollapsedProportion = expandedLeftProportion;
            owner.MaxWidth = double.PositiveInfinity;
            SetDockContentVisibility(owner, visible: true);
            adaptiveLeftCollapsed = false;
        }
        else if (ReferenceEquals(owner, rightTools) && adaptiveRightCollapsed)
        {
            owner.Proportion = expandedRightProportion;
            owner.CollapsedProportion = expandedRightProportion;
            owner.MaxWidth = double.PositiveInfinity;
            SetDockContentVisibility(owner, visible: true);
            adaptiveRightCollapsed = false;
        }
        else if (ReferenceEquals(owner, bottomTools) && adaptiveBottomCollapsed)
        {
            owner.Proportion = expandedBottomProportion;
            owner.CollapsedProportion = expandedBottomProportion;
            owner.MaxHeight = double.PositiveInfinity;
            SetDockContentVisibility(owner, visible: true);
            adaptiveBottomCollapsed = false;
        }
    }

    private static void SetDockContentVisibility(IToolDock dock, bool visible)
    {
        foreach (IDockable item in dock.VisibleDockables ?? [])
        {
            if (item.Context is Control content)
            {
                content.IsVisible = visible;
            }
        }
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

    private bool FocusNextRegion()
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
            IDockable target = documents.ActiveDockable ?? overviewDocument;
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

    private static T FindDockable<T>(IDockable root, string id)
        where T : class, IDockable =>
        FindDockable(root, id) as T ?? throw new InvalidOperationException(
            $"The Dock graph is missing required element '{id}'.");

    private static IDockable? FindDockable(IDockable root, string id)
    {
        HashSet<IDockable> visited = new(ReferenceEqualityComparer.Instance);
        Stack<IDockable> pending = new();
        pending.Push(root);
        while (pending.TryPop(out IDockable? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.Id == id)
            {
                return current;
            }

            if (current is IDock dock)
            {
                foreach (IDockable child in dock.VisibleDockables ?? [])
                {
                    pending.Push(child);
                }
            }

            if (current is IRootDock rootDock)
            {
                foreach (IDockable child in (rootDock.HiddenDockables ?? [])
                             .Concat(rootDock.LeftPinnedDockables ?? [])
                             .Concat(rootDock.RightPinnedDockables ?? [])
                             .Concat(rootDock.TopPinnedDockables ?? [])
                             .Concat(rootDock.BottomPinnedDockables ?? []))
                {
                    pending.Push(child);
                }

                foreach (IDockWindow window in rootDock.Windows ?? [])
                {
                    if (window.Layout is not null)
                    {
                        pending.Push(window.Layout);
                    }
                }
            }
        }

        return null;
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
            fileStatus.Text = "Workspace operation cancelled.";
            gitStatus.Text = "Workspace operation cancelled.";
        }
        catch (Exception exception)
        {
            fileStatus.Text = exception.Message;
            gitStatus.Text = exception.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private sealed record SearchChoice(WorkspaceTextMatchView Match, GoalId? GoalId)
    {
        public override string ToString() => $"{Match.Path}:{Match.LineNumber}  {Match.Text}";
    }

    internal sealed record FileTreeNode(
        string Name,
        WorkbenchDocumentPath? Path,
        IReadOnlyList<FileTreeNode> Children);

    private sealed class FileTreeBuilder(string name)
    {
        private readonly Dictionary<string, FileTreeBuilder> directories =
            new(StringComparer.Ordinal);

        internal List<FileTreeNode> Files { get; } = [];
        internal string Name { get; } = name;

        internal FileTreeBuilder Directory(string directory)
        {
            if (!directories.TryGetValue(directory, out FileTreeBuilder? child))
            {
                child = new(directory);
                directories.Add(directory, child);
            }

            return child;
        }

        internal IReadOnlyList<FileTreeNode> ToNodes() =>
            directories.Values
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .Select(directory => new FileTreeNode(
                    directory.Name,
                    Path: null,
                    directory.ToNodes()))
                .Concat(Files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
    }

    private sealed record ChangeChoice(WorkspaceGitFileChangeView Change, GoalId? GoalId)
    {
        public override string ToString() => $"{Change.Status}  {Change.Path}";
    }

    private sealed record RunOutputChoice(RunOutputView Output)
    {
        public override string ToString()
        {
            string exit = Output.Result?.ExitCode is { } code ? $" · exit {code}" : string.Empty;
            return $"{Output.Operation} · {Output.State}{exit} · {Output.StartedAt.LocalDateTime:g}";
        }
    }

    private sealed record ProblemChoice(
        WorkbenchCodeDiagnostic Diagnostic,
        GoalId? GoalId)
    {
        public override string ToString()
        {
            int line = Diagnostic.Range.Start.Line + 1;
            int column = Diagnostic.Range.Start.Character + 1;
            return $"{Diagnostic.Severity} {Diagnostic.Id.Value}  " +
                   $"{Diagnostic.Path.Value}:{line}:{column}  {Diagnostic.Message.Value}";
        }
    }

    private sealed record SymbolDestinationChoice(
        WorkbenchCodeSymbolDestination Destination)
    {
        public override string ToString()
        {
            int line = Destination.Range?.Start.Line + 1 ?? 0;
            return $"{Destination.Path?.Value}:{line}  {Destination.Display.Value}";
        }
    }

    private sealed class UiLoadProgress(TextBlock status) : IProgress<WorkbenchCodeLoadProgress>
    {
        public void Report(WorkbenchCodeLoadProgress value) => Dispatcher.UIThread.Post(() =>
            status.Text = $"{value.Stage} · {value.Message.Value}");
    }

}
