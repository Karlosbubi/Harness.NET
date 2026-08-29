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
    private readonly IWorkbenchDocumentService documentService;
    private readonly IWorkbenchCodeIntelligenceService codeIntelligenceService;
    private readonly IWorkspaceMutationService? mutationService;
    private readonly IWorkbenchLayoutService layoutService;
    private readonly IWorkbenchDocumentPrompt documentPrompt;
    private readonly Func<AvaloniaShellState> state;
    private readonly Func<bool, Task> manageWorkspace;
    private readonly Func<string, Task> manageWorkspaceAt;
    private readonly Func<Task> manageProjectSecrets;
    private readonly Func<Task> refreshWorkspaceContext;
    private readonly IDeveloperProjectExecutionService? developerExecutionService;
    private readonly CancellationToken cancellationToken;
    private readonly Factory factory = new();
    private readonly WorkbenchDockLayoutCodec layoutCodec;
    private readonly Dictionary<string, Control> durableContexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceDocumentSession> sourceDocuments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextEditor> virtualDocuments = new(StringComparer.Ordinal);
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
    private readonly ProblemsTool problemsToolUnit;
    private readonly DocumentIntelligence documentIntelligence;
    private TextBlock GitStatus => gitChangesTool.Status;
    private string GitFingerprint => gitChangesTool.Fingerprint;
    private WorkbenchWorkspaceContext? CurrentGitContext => gitChangesTool.CurrentContext;
    private string? workspaceId;
    private string? selectedGoalId;
    private bool busy;
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
    private KeybindingSettingsSnapshot keybindingSettings = KeybindingSettingsSnapshot.Default;
    private static readonly KeybindingCommand[] EditorKeyCommands =
    [
        KeybindingCommand.SaveDocument,
        KeybindingCommand.CloseDocument,
        KeybindingCommand.ShowCompletion,
        KeybindingCommand.ShowQuickInfo,
        KeybindingCommand.GoToDefinition,
        KeybindingCommand.FindReferences,
        KeybindingCommand.FindImplementations,
        KeybindingCommand.RenameSymbol,
        KeybindingCommand.FormatDocument,
        KeybindingCommand.FormatSelection,
        KeybindingCommand.OrganizeImports,
        KeybindingCommand.ShowQuickFixes,
    ];
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
        this.documentService = documentService;
        this.codeIntelligenceService = codeIntelligenceService;
        this.mutationService = mutationService;
        this.layoutService = layoutService;
        this.documentPrompt = documentPrompt;
        this.state = state;
        this.manageWorkspace = manageWorkspace ?? (_ => Task.CompletedTask);
        this.manageWorkspaceAt = manageWorkspaceAt ?? (_ => Task.CompletedTask);
        this.manageProjectSecrets = manageProjectSecrets ?? (() => Task.CompletedTask);
        this.developerExecutionService = developerExecutionService;
        this.developerGitService = developerGitService;
        this.refreshWorkspaceContext = refreshWorkspaceContext ?? (() => Task.CompletedTask);
        this.cancellationToken = cancellationToken;
        factory.HideToolsOnClose = true;
        layoutCodec = new(factory);
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
        gitConflictsTool = new(
            toolContext,
            codeIntelligenceService,
            gitChangesTool.Render,
            HasOpenSourceDocument);
        runOutputToolUnit = new(toolContext, runOutputService, developerExecutionService);
        problemsToolUnit = new(NavigateToProblemAsync);
        documentIntelligence = new(
            codeIntelligenceService,
            developerExecutionService,
            ActiveWorkspace,
            () => sourceDocuments,
            problemsToolUnit,
            cancellationToken);

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

        documents = documentDock ?? throw new InvalidOperationException("Dock did not create the document region.");
        leftTools = left;
        rightTools = right;
        bottomTools = bottom;
        overviewDocument = overview ?? throw new InvalidOperationException("Dock did not create the overview document.");
        left!.WithProportion(0.19);
        right!.WithProportion(0.22);
        bottom!.WithProportion(0.45);
        root = rootDock ?? throw new InvalidOperationException("Dock did not create the workbench root.");
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
        WorkbenchDockContent.Attach(navigationTool!, navigation);
        WorkbenchDockContent.Attach(filesDockTool!, files);
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
    internal int VirtualDocumentCount => virtualDocuments.Count;
    internal TreeView FileTree => filesTool.Tree;
    internal TextBox FileFilter => filesTool.Filter;
    internal TextEditor? ActiveSourceEditor => activeDocument?.Id is { } id &&
                                               sourceDocuments.TryGetValue(id, out SourceDocumentSession? session)
        ? session.NativeEditor
        : null;
    internal TextEditor? ActiveVirtualEditor => activeDocument?.Id is { } id &&
                                                virtualDocuments.TryGetValue(id, out TextEditor? editor)
        ? editor
        : activeDocument?.Context as TextEditor;
    internal ListBox Problems => problemsToolUnit.List;
    internal string? ProblemsStatusText => problemsToolUnit.Status.Text;
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
            await filesTool.RefreshAsync();
            await RefreshGitAsync();
        }
    }

    internal async ValueTask<bool> PrepareForShutdownAsync()
    {
        if (!await gitConflictsTool.ResolveUnsavedAsync(WorkbenchDocumentTransition.Exit)) return false;
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
        await InvalidateCodeIntelligenceAsync();
        return true;
    }

    internal async ValueTask<bool> PrepareForWorkspaceChangeAsync()
    {
        if (!await gitConflictsTool.ResolveUnsavedAsync(WorkbenchDocumentTransition.Switch)) return false;
        return await CloseAllSourceDocumentsAsync(WorkbenchDocumentTransition.Switch);
    }

    internal void Update(AvaloniaShellState snapshot)
    {
        filesTool.Update(snapshot);
        documentIntelligence.UpdatePreferences(snapshot.Settings
            .EditorIntelligenceSettings?.Preferences ?? EditorIntelligencePreferences.Default);
        KeybindingSettingsSnapshot nextKeybindings = snapshot.Settings.KeybindingSettings ??
                                                     KeybindingSettingsSnapshot.Default;
        bool keybindingsChanged = nextKeybindings != keybindingSettings;
        keybindingSettings = nextKeybindings;
        foreach (SourceDocumentSession session in sourceDocuments.Values)
        {
            session.Editor.ApplyTheme();
            if (keybindingsChanged)
            {
                session.Surface.ApplyKeybindings(keybindingSettings);
                session.Vim.SetInputMode(keybindingSettings.InputMode);
            }
        }

        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        if (!string.Equals(workspaceId, active?.Id, StringComparison.Ordinal))
        {
            workspaceId = active?.Id;
            Dispatcher.UIThread.Post(async () =>
                await CloseAllSourceDocumentsAsync(WorkbenchDocumentTransition.Close));
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
            : new(new("document.open"), false, "document_open_failed", filesTool.StatusText);
    }

    /// <summary>
    /// Offers each Git-tracked file as a command that opens it. The catalog is loaded on
    /// demand so quick open reflects the same bounded, context-resolved file list the
    /// Files panel shows rather than a separate scan.
    /// </summary>
    internal async ValueTask<IReadOnlyList<PaletteCommand>> BuildFileCommandsAsync()
        => await filesTool.BuildFileCommandsAsync();

    private async ValueTask OpenFileAsync(string relativePath, GoalId? requestedGoalId)
    {
        WorkspaceView? active = ActiveWorkspace();
        if (busy || active is null || !active.IsTrusted || string.IsNullOrWhiteSpace(relativePath))
        {
            filesTool.ReportStatus(active is null
                ? "Select a workspace first."
                : active.IsTrusted
                    ? "Enter a relative file path."
                    : "Trust the workspace before reading files.");
            return;
        }
        if (gitConflictsTool.HasActiveDocument(relativePath.Trim(), requestedGoalId))
        {
            filesTool.ReportStatus("That path is active in the Git conflict result editor. " +
                                   "Save and stage it there before opening a second buffer.");
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
                filesTool.ReportStatus(file.Error);
                return;
            }

            string id = SourceDocumentId(file);
            if (sourceDocuments.TryGetValue(id, out SourceDocumentSession? existing))
            {
                if (await TrySwitchDocumentAsync(existing.Document))
                {
                    filesTool.ReportStatus($"Activated {file.Path.Value}.");
                }

                return;
            }

            if (!await PrepareActiveDocumentTransitionAsync(WorkbenchDocumentTransition.Switch))
            {
                filesTool.ReportStatus($"Kept unsaved changes; {file.Path.Value} was not opened.");
                return;
            }

            SourceDocumentSession session = CreateSourceDocument(id, file);
            documents.AddDocument(session.Document);
            SetActiveDocument(session.Document);
            filesTool.ReportStatus($"Opened {file.Path.Value} · {file.Size.Value:N0} bytes · " +
                                   file.AccessDescription.TrimEnd('.') +
                                   (file.IsTruncated ? " · truncated." : "."));
        });
    }

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

    private bool HasOpenSourceDocument(
        WorkbenchWorkspaceContext context,
        DeveloperGitPath path) => sourceDocuments.Values.Any(session =>
        session.View.WorkspaceId == context.WorkspaceId &&
        session.View.GoalId == context.GoalId &&
        session.View.Path.Value.Equals(path.Value, StringComparison.Ordinal));

    internal ValueTask SaveGitConflictResultAsync() => gitConflictsTool.SaveAsync();

    internal ValueTask StageSavedGitConflictResultAsync() => gitConflictsTool.StageAsync();

    internal ValueTask PreviewAndApplyGitDestructiveAsync(DeveloperGitDestructiveAction action) =>
        gitChangesTool.PreviewAndApplyDestructiveAsync(action);

    private bool IsOriginalDocumentDirty(string path)
    {
        SourceDocumentSession? session = sourceDocuments.Values.FirstOrDefault(item =>
            item.View.GoalId is null && item.View.Path.Value.Equals(path, StringComparison.Ordinal));
        session?.SynchronizeDirtyState();
        return session?.IsDirty == true;
    }

    private bool HasDirtyOriginalDocuments()
    {
        foreach (SourceDocumentSession session in sourceDocuments.Values.Where(item =>
                     item.View.GoalId is null))
            session.SynchronizeDirtyState();
        return sourceDocuments.Values.Any(item => item.View.GoalId is null && item.IsDirty);
    }

    private async ValueTask ReloadOriginalDocumentAsync(string path)
    {
        SourceDocumentSession? session = sourceDocuments.Values.FirstOrDefault(item =>
            item.View.GoalId is null && item.View.Path.Value.Equals(path, StringComparison.Ordinal));
        if (session is not null)
            await ReloadSourceDocumentAsync(session, confirmDiscard: false);
    }

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

            OpenOrReplaceDocument(
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
        if ((ActiveSourceEditor ?? ActiveVirtualEditor) is { } editor)
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
        await documentIntelligence.InvalidateAsync();
    }

    private async ValueTask NavigateToProblemAsync(
        WorkbenchCodeDiagnostic diagnostic,
        GoalId? goalId)
    {
        SourceDocumentSession? session = sourceDocuments.Values.FirstOrDefault(item =>
            item.View.GoalId == goalId &&
            item.View.Path.Value.Equals(diagnostic.Path.Value, StringComparison.Ordinal));
        if (session is null)
        {
            await OpenFileAsync(diagnostic.Path.Value, goalId);
            session = sourceDocuments.Values.FirstOrDefault(item =>
                item.View.GoalId == goalId &&
                item.View.Path.Value.Equals(diagnostic.Path.Value, StringComparison.Ordinal));
        }

        if (session is null)
        {
            return;
        }

        SetActiveDocument(session.Document);
        WorkbenchCodePosition position = diagnostic.Range.Start;
        session.Editor.SetCaretPosition(position);
        session.Editor.ScrollTo(position);
        session.Editor.Focus();
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

    private SourceDocumentSession CreateSourceDocument(
        string id,
        WorkbenchDocumentView view)
    {
        SourceEditorSurface surface = SourceEditorSurface.Create(view, keybindingSettings);
        IWorkbenchEditorAdapter editor = surface.Editor;
        AutomationProperties.SetName(
            editor.Control,
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
            view,
            keybindingSettings.InputMode);
        document.CloseRequested = () => OnSourceDocumentCloseRequested(session);
        editor.TextChanged += (_, _) =>
        {
            session.CancelHover();
            editor.SetOccurrences([]);
            session.SynchronizeDirtyState();
            documentIntelligence.ScheduleDiagnostics(session);
            documentIntelligence.SchedulePresentation(session);
        };
        editor.CaretChanged += (_, _) => documentIntelligence.ScheduleOccurrences(session);
        editor.CodeLensInvoked += async (_, args) =>
            await InvokeCodeLensAsync(session, args.Lens);
        surface.CodeLensInvoked += async (_, args) =>
            await InvokeCodeLensAsync(session, args.Lens);
        editor.ViewportChanged += (_, _) => documentIntelligence.SchedulePresentation(
            session,
            includeStructure: false);
        editor.KeyDown += async (_, args) =>
        {
            KeybindingCommand? command = KeybindingInput.Match(
                args, keybindingSettings, EditorKeyCommands);
            if (command is not null)
            {
                args.Handled = true;
                await ExecuteEditorCommandAsync(session, command.Value);
                return;
            }
            if (session.Vim.ShouldHandle(args))
            {
                args.Handled = true;
                _ = session.Vim.Handle(args);
            }
        };
        editor.TextEntered += async (_, args) =>
        {
            await HandleTextEnteredAsync(session, args.Text);
        };
        editor.TextPasted += async (_, args) =>
        {
            await HandlePasteAsync(session, args.Range);
        };
        editor.PointerPositionChanged += (_, args) =>
        {
            if (args.Position is { } position)
            {
                _ = ShowQuickInfoOnHoverAsync(
                    session,
                    position,
                    session.BeginHover(cancellationToken));
            }
        };
        editor.PointerExited += (_, _) => session.CancelHover();
        surface.Save.Click += async (_, _) => await SaveSourceDocumentAsync(session);
        surface.Reload.Click += async (_, _) => await ReloadSourceDocumentAsync(session, confirmDiscard: true);
        surface.Close.Click += async (_, _) => await RequestSourceDocumentCloseAsync(session);
        surface.Completion.Click += async (_, _) => await ShowCompletionAsync(
            session, WorkbenchCodeCompletionTriggerKind.Invoke, triggerCharacter: null);
        surface.WorkspaceSymbols.Click += async (_, _) => await ShowWorkspaceSymbolsAsync(session);
        surface.SymbolInfo.Click += async (_, _) => await ShowQuickInfoAsync(session);
        surface.Definition.Click += async (_, _) => await NavigateSymbolAsync(
            session, SemanticNavigationKind.Definition);
        surface.References.Click += async (_, _) => await NavigateSymbolAsync(
            session, SemanticNavigationKind.References);
        surface.Implementations.Click += async (_, _) => await NavigateSymbolAsync(
            session, SemanticNavigationKind.Implementations);
        surface.InspectionRequested += async kind => await ShowInspectionAsync(session, kind);
        surface.FormatDocument.Click += async (_, _) => await TransformDocumentAsync(
            session, WorkbenchCodeDocumentTransformationKind.FormatDocument);
        surface.FormatSelection.Click += async (_, _) => await TransformDocumentAsync(
            session, WorkbenchCodeDocumentTransformationKind.FormatSelection);
        surface.FormatChangedSpans.Click += async (_, _) => await TransformDocumentAsync(
            session, WorkbenchCodeDocumentTransformationKind.FormatChangedSpans);
        surface.OrganizeImports.Click += async (_, _) => await TransformDocumentAsync(
            session, WorkbenchCodeDocumentTransformationKind.OrganizeImports);
        surface.RemoveUnusedImports.Click += async (_, _) => await TransformDocumentAsync(
            session, WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports);
        surface.QuickFix.Click += async (_, _) => await ShowImportFixesAsync(session);
        surface.NavigationRequested += position =>
        {
            editor.SetCaretPosition(position);
            editor.ScrollTo(position);
            editor.Focus();
        };
        sourceDocuments.Add(id, session);
        session.SynchronizeDirtyState();
        documentIntelligence.ScheduleDiagnostics(session, immediate: true);
        documentIntelligence.SchedulePresentation(session, immediate: true);
        return session;
    }

    private async ValueTask ShowWorkspaceSymbolsAsync(SourceDocumentSession session)
    {
        if (!DocumentIntelligence.CanUse(session) || OwnerWindow() is not { } owner)
        {
            return;
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null || !session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                session, codeSession, version);
            WorkspaceSymbolSearchDialog dialog = new(
                async (value, searchCancellation) =>
                {
                    using CancellationTokenSource linked =
                        CancellationTokenSource.CreateLinkedTokenSource(token, searchCancellation);
                    return await codeIntelligenceService.SearchSymbolsAsync(
                        new(snapshot, value, MaximumResults: 200, Offset: 0), linked.Token);
                },
                destination => NavigateToSymbolAsync(destination, session.View.GoalId));
            await dialog.ShowDialog(owner);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async ValueTask ShowCompletionAsync(
        SourceDocumentSession session,
        WorkbenchCodeCompletionTriggerKind triggerKind,
        char? triggerCharacter)
    {
        if (!DocumentIntelligence.CanUse(session))
        {
            return;
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null || !session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
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
            CompletionWindow window = new RoslynCompletionWindow(session.NativeEditor.TextArea)
            {
                StartOffset = session.Editor.GetOffset(result.ApplicableRange.Start),
                EndOffset = session.Editor.GetOffset(result.ApplicableRange.End),
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
            !DocumentIntelligence.CanUse(session) || OwnerWindow() is not { } owner)
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

    internal ValueTask TransformActiveDocumentAsync(
        WorkbenchCodeDocumentTransformationKind kind)
    {
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return ValueTask.CompletedTask;
        }

        return TransformDocumentAsync(session, kind);
    }

    internal ValueTask InspectActiveDocumentAsync(WorkbenchCodeInspectionKind kind)
    {
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return ValueTask.CompletedTask;
        }

        return ShowInspectionAsync(session, kind);
    }

    internal ValueTask ShowActiveQuickFixesAsync()
    {
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return ValueTask.CompletedTask;
        }

        return ShowImportFixesAsync(session);
    }

    internal ValueTask ApplyActiveCodeActionAsync(WorkbenchCodeActionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return ValueTask.CompletedTask;
        }

        return TransformDocumentAsync(
            session,
            WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
            codeActionId: candidate.Id,
            codeActionScope: candidate.Scope);
    }

    internal ValueTask HandleActiveTextEnteredAsync(string? text)
    {
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return ValueTask.CompletedTask;
        }

        return HandleTextEnteredAsync(session, text);
    }

    internal ValueTask HandleActivePasteAsync(WorkbenchCodeRange range)
    {
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return ValueTask.CompletedTask;
        }

        return HandlePasteAsync(session, range);
    }

    private ValueTask HandlePasteAsync(
        SourceDocumentSession session,
        WorkbenchCodeRange range) => documentIntelligence.Preferences.FormatOnPaste
        ? TransformDocumentAsync(
            session,
            WorkbenchCodeDocumentTransformationKind.FormatPaste,
            range: range,
            formattingTrigger: WorkbenchCodeFormattingTrigger.Paste,
            automatic: true)
        : ValueTask.CompletedTask;

    private async ValueTask HandleTextEnteredAsync(
        SourceDocumentSession session,
        string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        IWorkbenchEditorAdapter editor = session.Editor;
        if (text.Length > 1)
        {
            return;
        }

        char value = text[0];
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

        if (documentIntelligence.Preferences.FormatOnType &&
            FormattingTrigger(value) is { } formattingTrigger)
        {
            int end = editor.CaretOffset;
            int start = Math.Max(0, end - 1);
            await TransformDocumentAsync(
                session,
                WorkbenchCodeDocumentTransformationKind.FormatOnType,
                range: new(editor.GetPosition(start), editor.GetPosition(end)),
                formattingTrigger: formattingTrigger,
                automatic: true);
        }
    }

    internal bool CanTransformActiveDocument(WorkbenchCodeDocumentTransformationKind kind) =>
        activeDocument?.Id is { } id &&
        sourceDocuments.TryGetValue(id, out SourceDocumentSession? session) &&
        session.View.Access is WorkbenchDocumentAccess.Editable &&
        DocumentIntelligence.CanUse(session) &&
        (kind is not WorkbenchCodeDocumentTransformationKind.FormatSelection ||
            session.Editor.SelectionRange is not null);

    internal bool CanInvokeActiveEditorCommand(KeybindingCommand command)
    {
        if (activeDocument?.Id is not { } id ||
            !sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            return false;
        }

        return command switch
        {
            KeybindingCommand.CloseDocument => true,
            KeybindingCommand.SaveDocument =>
                session.View.Access is WorkbenchDocumentAccess.Editable,
            KeybindingCommand.ShowCompletion or KeybindingCommand.ShowQuickInfo or
                KeybindingCommand.GoToDefinition or KeybindingCommand.FindReferences or
                KeybindingCommand.FindImplementations => DocumentIntelligence.CanUse(session),
            KeybindingCommand.RenameSymbol => mutationService is not null &&
                session.View.Access is WorkbenchDocumentAccess.Editable &&
                DocumentIntelligence.CanUse(session),
            KeybindingCommand.FormatDocument => CanTransformActiveDocument(
                WorkbenchCodeDocumentTransformationKind.FormatDocument),
            KeybindingCommand.FormatSelection => CanTransformActiveDocument(
                WorkbenchCodeDocumentTransformationKind.FormatSelection),
            KeybindingCommand.OrganizeImports => CanTransformActiveDocument(
                WorkbenchCodeDocumentTransformationKind.OrganizeImports),
            KeybindingCommand.ShowQuickFixes => CanTransformActiveDocument(
                WorkbenchCodeDocumentTransformationKind.AddMissingImport),
            _ => false,
        };
    }

    internal async ValueTask InvokeActiveEditorCommandAsync(KeybindingCommand command)
    {
        if (activeDocument?.Id is { } id &&
            sourceDocuments.TryGetValue(id, out SourceDocumentSession? session))
        {
            await ExecuteEditorCommandAsync(session, command);
        }
    }

    private async ValueTask ExecuteEditorCommandAsync(
        SourceDocumentSession session,
        KeybindingCommand command)
    {
        switch (command)
        {
            case KeybindingCommand.SaveDocument:
                await SaveSourceDocumentAsync(session);
                break;
            case KeybindingCommand.CloseDocument:
                await RequestSourceDocumentCloseAsync(session);
                break;
            case KeybindingCommand.ShowCompletion:
                await ShowCompletionAsync(session, WorkbenchCodeCompletionTriggerKind.Invoke, null);
                break;
            case KeybindingCommand.ShowQuickInfo:
                await ShowQuickInfoAsync(session);
                break;
            case KeybindingCommand.GoToDefinition:
                await NavigateSymbolAsync(session, SemanticNavigationKind.Definition);
                break;
            case KeybindingCommand.FindReferences:
                await NavigateSymbolAsync(session, SemanticNavigationKind.References);
                break;
            case KeybindingCommand.FindImplementations:
                await NavigateSymbolAsync(session, SemanticNavigationKind.Implementations);
                break;
            case KeybindingCommand.RenameSymbol:
                await RenameSymbolAsync(session);
                break;
            case KeybindingCommand.FormatDocument:
                await TransformDocumentAsync(
                    session, WorkbenchCodeDocumentTransformationKind.FormatDocument);
                break;
            case KeybindingCommand.FormatSelection:
                await TransformDocumentAsync(
                    session, WorkbenchCodeDocumentTransformationKind.FormatSelection);
                break;
            case KeybindingCommand.OrganizeImports:
                await TransformDocumentAsync(
                    session, WorkbenchCodeDocumentTransformationKind.OrganizeImports);
                break;
            case KeybindingCommand.ShowQuickFixes:
                await ShowImportFixesAsync(session);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private async ValueTask TransformDocumentAsync(
        SourceDocumentSession session,
        WorkbenchCodeDocumentTransformationKind kind,
        WorkbenchCodeImportNamespace? importNamespace = null,
        WorkbenchCodeRange? range = null,
        WorkbenchCodeFormattingTrigger? formattingTrigger = null,
        WorkbenchCodeActionId? codeActionId = null,
        WorkbenchCodeActionScope? codeActionScope = null,
        bool automatic = false)
    {
        if (session.View.Access is not WorkbenchDocumentAccess.Editable ||
            !DocumentIntelligence.CanUse(session))
        {
            session.SetStatus("Formatting requires an editable C# source document.");
            return;
        }

        range ??= kind is WorkbenchCodeDocumentTransformationKind.FormatSelection
            ? session.Editor.SelectionRange
            : null;
        if (kind is WorkbenchCodeDocumentTransformationKind.FormatSelection && range is null)
        {
            session.SetStatus("Select the C# code to format first.");
            return;
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        if (!automatic)
        {
            session.SetBusy(true, kind switch
            {
                WorkbenchCodeDocumentTransformationKind.FormatDocument =>
                    "Formatting the document with Roslyn…",
                WorkbenchCodeDocumentTransformationKind.FormatSelection =>
                    "Formatting the selected code with Roslyn…",
                WorkbenchCodeDocumentTransformationKind.FormatChangedSpans =>
                    "Formatting changed code with Roslyn…",
                WorkbenchCodeDocumentTransformationKind.OrganizeImports =>
                    "Organizing imports with Roslyn…",
                WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports =>
                    "Removing unused imports with Roslyn…",
                WorkbenchCodeDocumentTransformationKind.AddMissingImport =>
                    $"Adding {importNamespace?.Value} with Roslyn…",
                WorkbenchCodeDocumentTransformationKind.ApplyCodeAction =>
                    "Applying the selected Roslyn code action…",
                _ => "Preparing deterministic transformation…",
            });
        }
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null || !session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                session, codeSession, version);
            WorkbenchCodeDocumentTransformationPreviewView preview =
                await codeIntelligenceService.PreviewDocumentTransformationAsync(
                    new(snapshot, kind, range, importNamespace, formattingTrigger,
                        codeActionId, codeActionScope), token);
            if (!session.IsCurrentInteraction(version))
            {
                session.SetStatus("The buffer changed before the transformation could be applied.");
                return;
            }

            if (preview.Disposition is not WorkbenchCodeTransformationDisposition.Ready ||
                preview.Fingerprint is null || preview.Edits.Count == 0)
            {
                session.SetStatus(preview.Conflicts.FirstOrDefault()?.Message.Value ??
                    preview.Issues.FirstOrDefault()?.Message.Value ??
                    "Roslyn could not prepare the requested transformation.");
                return;
            }

            if (preview.Edits.Count != 1 ||
                !preview.Edits[0].Path.Value.Equals(session.View.Path.Value, StringComparison.Ordinal))
            {
                await ApplyAtomicDocumentTransformationAsync(
                    session,
                    version,
                    kind,
                    range,
                    importNamespace,
                    formattingTrigger,
                    codeActionId,
                    codeActionScope,
                    preview,
                    token);
                return;
            }

            WorkbenchCodeDocumentTransformationEdit edit = preview.Edits[0];
            if (!string.Equals(session.Editor.Text, edit.OriginalText.Value,
                StringComparison.Ordinal))
            {
                session.SetStatus("The buffer changed before the transformation could be applied.");
                return;
            }

            if (edit.ReplacementCount == 0)
            {
                session.SetStatus(kind switch
                {
                    WorkbenchCodeDocumentTransformationKind.OrganizeImports =>
                        "Imports are already organized.",
                    WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports =>
                        "No compiler-proven unused imports were found.",
                    WorkbenchCodeDocumentTransformationKind.AddMissingImport =>
                        "The selected import is already present.",
                    WorkbenchCodeDocumentTransformationKind.FormatChangedSpans =>
                        "Changed code is already formatted.",
                    WorkbenchCodeDocumentTransformationKind.FormatPaste or
                        WorkbenchCodeDocumentTransformationKind.FormatOnType =>
                        "No automatic formatting was needed.",
                    WorkbenchCodeDocumentTransformationKind.ApplyCodeAction =>
                        "The selected code action no longer changes this document.",
                    _ => "The requested code is already formatted.",
                });
                return;
            }

            int caret = session.Editor.CaretOffset;
            session.Editor.Replace(0, session.Editor.TextLength, edit.Text.Value);
            session.Editor.CaretOffset = MapTransformedOffset(
                edit.OriginalText.Value,
                edit.Text.Value,
                caret);
            session.Editor.Focus();
            session.SetStatus(kind switch
            {
                WorkbenchCodeDocumentTransformationKind.FormatDocument =>
                    $"Formatted document · {edit.ReplacementCount:N0} Roslyn edit(s) · undo available.",
                WorkbenchCodeDocumentTransformationKind.FormatSelection =>
                    $"Formatted selection · {edit.ReplacementCount:N0} Roslyn edit(s) · undo available.",
                WorkbenchCodeDocumentTransformationKind.FormatChangedSpans =>
                    $"Formatted changed code · {edit.ReplacementCount:N0} Roslyn edit(s) · undo available.",
                WorkbenchCodeDocumentTransformationKind.FormatPaste =>
                    "Formatted pasted code with Roslyn · undo available.",
                WorkbenchCodeDocumentTransformationKind.FormatOnType =>
                    "Formatted current code with Roslyn · undo available.",
                WorkbenchCodeDocumentTransformationKind.OrganizeImports =>
                    $"Organized imports · {edit.ReplacementCount:N0} Roslyn edit(s) · undo available.",
                WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports =>
                    $"Removed unused imports · {edit.ReplacementCount:N0} Roslyn edit(s) · undo available.",
                WorkbenchCodeDocumentTransformationKind.AddMissingImport =>
                    $"Added using {importNamespace?.Value} · undo available.",
                WorkbenchCodeDocumentTransformationKind.ApplyCodeAction =>
                    codeActionScope is WorkbenchCodeActionScope.Document
                        ? $"Applied Roslyn fix to this document · {edit.ReplacementCount:N0} edit(s) · undo available."
                        : $"Applied Roslyn quick fix · {edit.ReplacementCount:N0} edit(s) · undo available.",
                _ => "Applied deterministic Roslyn transformation to the live buffer.",
            });
            documentIntelligence.ScheduleDiagnostics(session, immediate: true);
            documentIntelligence.SchedulePresentation(session, immediate: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            session.SetStatus("Document transformation cancelled.");
        }
        finally
        {
            if (!automatic)
            {
                session.SetBusy(false);
            }
        }
    }

    private async ValueTask ApplyAtomicDocumentTransformationAsync(
        SourceDocumentSession session,
        WorkbenchCodeBufferVersion version,
        WorkbenchCodeDocumentTransformationKind kind,
        WorkbenchCodeRange? range,
        WorkbenchCodeImportNamespace? importNamespace,
        WorkbenchCodeFormattingTrigger? formattingTrigger,
        WorkbenchCodeActionId? codeActionId,
        WorkbenchCodeActionScope? codeActionScope,
        WorkbenchCodeDocumentTransformationPreviewView preview,
        CancellationToken token)
    {
        if (mutationService is null || session.View.GoalId is null ||
            session.View.Sha256 is null)
        {
            session.SetStatus(
                "This refactoring changes another file and requires an approved goal worktree.");
            return;
        }

        WorkbenchCodeDocumentTransformationEdit? dirtyAffected = preview.Edits
            .FirstOrDefault(edit => sourceDocuments.Values.Any(open =>
                open.View.GoalId == session.View.GoalId &&
                open.View.Path.Value.Equals(edit.Path.Value, StringComparison.Ordinal) &&
                !open.Editor.Text.Equals(edit.OriginalText.Value, StringComparison.Ordinal)));
        if (dirtyAffected is not null)
        {
            session.SetStatus(
                $"Save or revert unsaved changes in {dirtyAffected.Path.Value} before applying this refactoring.");
            return;
        }

        DocumentTransformationPreviewRequest request = new(
            session.View.GoalId.Value,
            new(session.View.Path.Value),
            new(session.View.Sha256.Value),
            version,
            new(session.Editor.Text),
            session.Editor.CaretPosition,
            kind,
            range,
            DocumentTransformationOrigin.Human,
            [],
            importNamespace,
            formattingTrigger,
            codeActionId,
            codeActionScope);
        session.SetStatus(
            $"Applying Roslyn refactoring atomically to {preview.Edits.Count:N0} file(s)…");
        DocumentTransformationApplyView result =
            await mutationService.ApplyDocumentTransformationAsync(new(
                request,
                NewEditCorrelation(),
                preview.Fingerprint!), token);
        if (result.ErrorCode is not null || result.Preview is null)
        {
            session.SetStatus(result.Error ?? "The multi-file refactoring was not applied.");
            return;
        }

        foreach (WorkbenchCodeDocumentTransformationEdit appliedEdit in result.Preview.Edits)
        {
            SourceDocumentSession? open = sourceDocuments.Values.FirstOrDefault(candidate =>
                candidate.View.GoalId == session.View.GoalId &&
                candidate.View.Path.Value.Equals(appliedEdit.Path.Value, StringComparison.Ordinal));
            FileEditView? evidence = result.Files.FirstOrDefault(file =>
                file.Path.Equals(appliedEdit.Path.Value, StringComparison.Ordinal));
            if (open is null || evidence?.NewSha256 is null)
            {
                continue;
            }

            open.ReplaceWith(open.View with
            {
                Content = new(appliedEdit.Text.Value),
                Sha256 = new(evidence.NewSha256),
                Size = new(evidence.BytesWritten),
            });
            open.SetStatus("Applied compiler-verified atomic Roslyn refactoring.");
        }

        session.SetStatus(
            $"Applied Roslyn refactoring atomically to {result.Files.Count:N0} file(s).");
        await InvalidateCodeIntelligenceAsync();
        documentIntelligence.ScheduleDiagnostics(session, immediate: true);
    }

    private static WorkbenchCodeFormattingTrigger? FormattingTrigger(char value) => value switch
    {
        ';' => WorkbenchCodeFormattingTrigger.Semicolon,
        '}' => WorkbenchCodeFormattingTrigger.CloseBrace,
        '\n' or '\r' => WorkbenchCodeFormattingTrigger.NewLine,
        _ => null,
    };

    private static int MapTransformedOffset(string original, string candidate, int offset)
    {
        int bounded = Math.Clamp(offset, 0, original.Length);
        int prefix = 0;
        int prefixLimit = Math.Min(original.Length, candidate.Length);
        while (prefix < prefixLimit && original[prefix] == candidate[prefix])
        {
            prefix++;
        }

        if (bounded <= prefix)
        {
            return bounded;
        }

        int suffix = 0;
        while (suffix < original.Length - prefix && suffix < candidate.Length - prefix &&
               original[original.Length - suffix - 1] == candidate[candidate.Length - suffix - 1])
        {
            suffix++;
        }

        int originalChangedEnd = original.Length - suffix;
        int candidateChangedEnd = candidate.Length - suffix;
        if (bounded >= originalChangedEnd)
        {
            return Math.Clamp(candidateChangedEnd + bounded - originalChangedEnd,
                0, candidate.Length);
        }

        return Math.Clamp(prefix + Math.Min(bounded - prefix, candidateChangedEnd - prefix),
            0, candidate.Length);
    }

    private async ValueTask ShowImportFixesAsync(SourceDocumentSession session)
    {
        if (session.View.Access is not WorkbenchDocumentAccess.Editable ||
            !DocumentIntelligence.CanUse(session))
        {
            session.SetStatus("Quick fixes require an editable C# source document.");
            return;
        }

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        session.SetBusy(true, "Finding Roslyn fixes at the caret…");
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null || !session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                session, codeSession, version);
            WorkbenchCodeRange? codeActionRange = session.Editor.SelectionRange;
            WorkbenchCodeMissingImportView result =
                await codeIntelligenceService.GetMissingImportsAsync(snapshot, token);
            WorkbenchCodeActionView codeActions =
                await codeIntelligenceService.GetCodeActionsAsync(
                    new(snapshot, codeActionRange), token);
            if (!session.IsCurrentInteraction(version))
            {
                session.SetStatus("The buffer changed before quick fixes were ready.");
                return;
            }

            if (result.Candidates.Count == 0 && codeActions.Candidates.Count == 0)
            {
                session.SetStatus(codeActions.Issues.FirstOrDefault()?.Message.Value ??
                    result.Issues.FirstOrDefault()?.Message.Value ??
                    "No supported quick fix is available at the caret.");
                return;
            }

            StackPanel choices = new() { Spacing = 4, Margin = new Thickness(4) };
            Flyout flyout = new() { Content = choices };
            foreach (WorkbenchCodeMissingImportCandidate candidate in result.Candidates)
            {
                Button action = new()
                {
                    Content = $"using {candidate.Namespace.Value};  ·  {candidate.Symbol.Value}",
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(action,
                    $"Add using {candidate.Namespace.Value} for {candidate.Symbol.Value}");
                action.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await TransformDocumentAsync(session,
                        WorkbenchCodeDocumentTransformationKind.AddMissingImport,
                        candidate.Namespace);
                };
                choices.Children.Add(action);
            }
            foreach (WorkbenchCodeActionCandidate candidate in codeActions.Candidates)
            {
                string suffix = candidate.Scope is WorkbenchCodeActionScope.Document
                    ? "  ·  Fix all in document"
                    : candidate.AffectedFileCount > 1 || !candidate.ChangesActiveDocument
                        ? $"  ·  {candidate.AffectedFileCount:N0} files · atomic"
                        : string.Empty;
                Button action = new()
                {
                    Content = candidate.Title.Value + suffix,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(action,
                    candidate.Scope is WorkbenchCodeActionScope.Document
                        ? $"{candidate.Title.Value}, fix all in document"
                        : candidate.AffectedFileCount > 1 || !candidate.ChangesActiveDocument
                            ? $"{candidate.Title.Value}, affects {candidate.AffectedFileCount:N0} files, atomic apply"
                        : candidate.Title.Value);
                action.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await TransformDocumentAsync(session,
                        WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                        range: codeActionRange,
                        codeActionId: candidate.Id,
                        codeActionScope: candidate.Scope);
                };
                choices.Children.Add(action);
            }
            flyout.ShowAt(session.Surface.QuickFix);
            int count = result.Candidates.Count + codeActions.Candidates.Count;
            session.SetStatus($"{count:N0} Roslyn quick fix(es) available.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            session.SetStatus("Quick-fix discovery cancelled.");
        }
        finally
        {
            session.SetBusy(false);
        }
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
            session.Editor.CaretPosition,
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
            documentIntelligence.ScheduleDiagnostics(active, immediate: true);
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
                         .OrderByDescending(value => session.Editor.GetOffset(value.Range.Start)))
            {
                int start = session.Editor.GetOffset(change.Range.Start);
                int end = session.Editor.GetOffset(change.Range.End);
                session.Editor.Replace(start, Math.Max(0, end - start), change.Text.Value);
            }

            if (result.NewPosition is { } position)
            {
                session.Editor.CaretOffset = session.Editor.GetOffset(position);
            }
            else if (result.Changes.LastOrDefault() is { } last)
            {
                session.Editor.CaretOffset =
                    session.Editor.GetOffset(last.Range.Start) + last.Text.Value.Length;
            }

            if (commitCharacter is { } value && value is not '\t' and not '\n' &&
                (session.Editor.CaretOffset >= session.Editor.TextLength ||
                 session.Editor.GetCharAt(session.Editor.CaretOffset) != value))
            {
                session.Editor.Insert(session.Editor.CaretOffset, value.ToString());
                session.Editor.CaretOffset++;
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
        if (!DocumentIntelligence.CanUse(session))
        {
            return;
        }

        session.CompletionWindow?.Hide();
        session.CompletionWindow = null;

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            WorkbenchCodeQuickInfoView result = await codeIntelligenceService.GetQuickInfoAsync(
                DocumentIntelligence.Snapshot(session, codeSession, version, requestedPosition), token);
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
            InsightWindow window = new(session.NativeEditor.TextArea)
            {
                Child = card,
                StartOffset = result.ApplicableRange is null
                    ? session.Editor.CaretOffset
                    : session.Editor.GetOffset(result.ApplicableRange.Start),
                EndOffset = result.ApplicableRange is null
                    ? session.Editor.CaretOffset
                    : session.Editor.GetOffset(result.ApplicableRange.End),
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
        if (!DocumentIntelligence.CanUse(session))
        {
            return;
        }

        session.CompletionWindow?.Hide();
        session.CompletionWindow = null;

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            WorkbenchCodeSignatureHelpView result =
                await codeIntelligenceService.GetSignatureHelpAsync(
                    DocumentIntelligence.Snapshot(session, codeSession, version), token);
            if (!session.IsCurrentInteraction(version) || result.Signatures.Count == 0)
            {
                return;
            }

            session.SignatureWindow?.Hide();
            OverloadInsightWindow window = new(session.NativeEditor.TextArea)
            {
                Provider = new RoslynOverloadProvider(result),
                StartOffset = Math.Max(0, session.Editor.CaretOffset - 1),
                EndOffset = session.Editor.TextLength,
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
        if (!DocumentIntelligence.CanUse(session))
        {
            return;
        }

        session.CloseInteractiveWindows();

        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
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

            WorkbenchCodeSymbolDestination[] navigable = result.Destinations
                .Where(destination =>
                    destination.Kind is WorkbenchCodeDestinationKind.Source &&
                    destination.Path is not null && destination.Range is not null ||
                    destination.VirtualDocumentId is not null)
                .ToArray();
            if (kind is not SemanticNavigationKind.References && navigable.Length == 1)
            {
                await NavigateToDestinationAsync(navigable[0], session);
                return;
            }

            if (navigable.Length == 0)
            {
                session.SetStatus(result.Destinations.FirstOrDefault()?.Display.Value ??
                    "No editable source destination is available for this symbol.");
                return;
            }

            session.SetStatus($"Found {navigable.Length:N0} navigable {NavigationLabel(kind)} " +
                              (navigable.Length == 1 ? "destination." : "destinations."));

            ListBox list = new()
            {
                ItemsSource = navigable.Select(destination => new SymbolDestinationChoice(destination))
                    .ToArray(),
                MaxHeight = 320,
                MinWidth = 420,
            };
            AutomationProperties.SetName(list,
                $"{navigable.Length} navigable {NavigationLabel(kind)} destinations for " +
                session.View.Path.Value);
            InsightWindow window = new(session.NativeEditor.TextArea)
            {
                Child = list,
                StartOffset = session.Editor.CaretOffset,
                EndOffset = session.Editor.CaretOffset,
            };
            list.SelectionChanged += async (_, _) =>
            {
                if (list.SelectedItem is SymbolDestinationChoice choice)
                {
                    window.Hide();
                    await NavigateToDestinationAsync(choice.Destination, session);
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

    private async ValueTask InvokeCodeLensAsync(
        SourceDocumentSession session,
        WorkbenchCodeLens lens)
    {
        session.Editor.SetCaretPosition(lens.Target);
        switch (lens.Kind)
        {
            case WorkbenchCodeLensKind.References:
                await NavigateSymbolAsync(session, SemanticNavigationKind.References);
                break;
            case WorkbenchCodeLensKind.Implementations:
                await NavigateSymbolAsync(session, SemanticNavigationKind.Implementations);
                break;
            case WorkbenchCodeLensKind.Tests:
                await ShowAssociatedTestsAsync(session);
                break;
            case WorkbenchCodeLensKind.Run:
                await RunCodeLensTargetAsync(session, lens);
                break;
            case WorkbenchCodeLensKind.Debug:
                session.SetStatus(developerExecutionService?.Capabilities.DebugStatus ??
                    "No typed debugger capability is available.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lens));
        }
    }

    private async ValueTask RunCodeLensTargetAsync(
        SourceDocumentSession session,
        WorkbenchCodeLens lens)
    {
        WorkspaceView? workspace = ActiveWorkspace();
        if (developerExecutionService is null || workspace is null || !workspace.IsTrusted ||
            lens.ExecutionTarget is null)
        {
            session.SetStatus("No validated project execution target is available.");
            return;
        }
        if (session.IsDirty)
        {
            session.SetStatus("Save this document before running its entry point.");
            return;
        }

        session.SetBusy(true, $"Starting {lens.ExecutionTarget.ProjectPath.Value}…");
        try
        {
            DeveloperExecutionStartResult started =
                await developerExecutionService.StartRunAsync(new(
                    WorkbenchRequest(workspace), lens.ExecutionTarget), cancellationToken);
            if (started.Execution is null)
            {
                session.SetStatus(started.Error ?? "The project run could not start.");
                return;
            }
            session.SetStatus($"Run {started.Execution.Id.Value[..8]} started for " +
                              $"{started.Execution.Target.ProjectPath.Value}.");
            ShowRunOutput();
            await RefreshRunOutputAsync();
            _ = PollDeveloperRunAsync(started.Execution.Id);
        }
        finally
        {
            session.SetBusy(false);
        }
    }

    private async Task PollDeveloperRunAsync(DeveloperExecutionId id)
    {
        if (developerExecutionService is null)
        {
            return;
        }
        try
        {
            for (int attempt = 0; attempt < 1200; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                WorkspaceView? workspace = ActiveWorkspace();
                if (workspace is null)
                {
                    return;
                }
                DeveloperExecutionListResult listed = await developerExecutionService.ListAsync(
                    WorkbenchRequest(workspace), cancellationToken);
                DeveloperExecutionView? execution = listed.Executions.FirstOrDefault(item =>
                    item.Id == id);
                if (execution is null || execution.State is not DeveloperExecutionState.Running)
                {
                    await RefreshRunOutputAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask ShowAssociatedTestsAsync(SourceDocumentSession session)
    {
        if (!DocumentIntelligence.CanUse(session))
        {
            return;
        }

        session.CloseInteractiveWindows();
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            session.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? codeSession = await documentIntelligence.EnsureSessionAsync(session, token);
            if (codeSession is null)
            {
                return;
            }

            session.SetStatus("Finding associated tests with Roslyn…");
            WorkbenchCodeSemanticView result = await codeIntelligenceService
                .FindAssociatedTestsAsync(new(
                    DocumentIntelligence.Snapshot(session, codeSession, version),
                    Query: null,
                    MaximumResults: 100,
                    Offset: 0), token);
            if (!session.IsCurrentInteraction(version))
            {
                return;
            }

            WorkbenchCodeSymbolDestination[] source = result.Items
                .Select(item => item.Destination)
                .Where(destination => destination.Kind is WorkbenchCodeDestinationKind.Source &&
                    destination.Path is not null && destination.Range is not null)
                .ToArray();
            if (source.Length == 0)
            {
                session.SetStatus("No associated source tests were found for this declaration.");
                return;
            }

            session.SetStatus($"Found {source.Length:N0} associated test" +
                              (source.Length == 1 ? "." : "s."));
            ListBox list = new()
            {
                ItemsSource = source.Select(destination => new SymbolDestinationChoice(destination))
                    .ToArray(),
                MaxHeight = 320,
                MinWidth = 420,
            };
            AutomationProperties.SetName(list,
                $"{source.Length} associated tests for {session.View.Path.Value}");
            InsightWindow window = new(session.NativeEditor.TextArea)
            {
                Child = list,
                StartOffset = session.Editor.CaretOffset,
                EndOffset = session.Editor.CaretOffset,
            };
            list.SelectionChanged += async (_, _) =>
            {
                if (list.SelectedItem is SymbolDestinationChoice choice)
                {
                    window.Hide();
                    await NavigateToSymbolAsync(choice.Destination, session.View.GoalId);
                }
            };
            session.QuickInfoWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            session.SetStatus($"Associated-test lookup failed · {exception.Message}");
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
        WorkbenchCodePosition position = destination.Range.Start;
        target.Editor.SetCaretPosition(position);
        target.Editor.ScrollTo(position);
        target.Editor.Focus();
    }

    private ValueTask NavigateToDestinationAsync(
        WorkbenchCodeSymbolDestination destination,
        SourceDocumentSession source) => destination.VirtualDocumentId is null
        ? NavigateToSymbolAsync(destination, source.View.GoalId)
        : OpenVirtualDocumentAsync(source, destination);

    private async ValueTask OpenVirtualDocumentAsync(
        SourceDocumentSession source,
        WorkbenchCodeSymbolDestination destination)
    {
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            source.BeginInteraction(cancellationToken);
        WorkbenchCodeSessionId? sessionId = await documentIntelligence.EnsureSessionAsync(source, token);
        if (sessionId is null || !source.IsCurrentInteraction(version)) return;
        WorkbenchCodeVirtualDocumentView virtualDocument =
            await codeIntelligenceService.GetVirtualDocumentAsync(new(
                DocumentIntelligence.Snapshot(source, sessionId, version),
                destination.VirtualDocumentId!), token);
        if (!source.IsCurrentInteraction(version) || virtualDocument.Text is null ||
            virtualDocument.Title is null || virtualDocument.Origin is null)
        {
            source.SetStatus(virtualDocument.Issues.FirstOrDefault()?.Message.Value ??
                "The virtual source document is unavailable.");
            return;
        }

        string id = $"virtual:{sessionId.Value}:{virtualDocument.Id.Value}";
        if (virtualDocuments.TryGetValue(id, out TextEditor? existing))
        {
            IDockable? existingDocument = documents.VisibleDockables?
                .FirstOrDefault(item => item.Id == id);
            if (existingDocument is not null) SetActiveDocument(existingDocument);
            existing.Focus();
            return;
        }
        if (!await PrepareActiveDocumentTransitionAsync(WorkbenchDocumentTransition.Switch)) return;

        TextEditor editor = CodeEditorView.Create(
            virtualDocument.Text.Value,
            isReadOnly: true,
            wordWrap: false,
            showLineNumbers: true,
            path: "virtual.cs");
        AutomationProperties.SetName(editor,
            $"Read-only {VirtualKindLabel(virtualDocument.Kind)} for {virtualDocument.Title.Value}");
        TextBlock identity = new()
        {
            Text = $"{VirtualKindLabel(virtualDocument.Kind)} · read-only · " +
                   $"{virtualDocument.Origin.Project.Value} · " +
                   $"{virtualDocument.Origin.TargetFramework.Value} · " +
                   $"{virtualDocument.Origin.Configuration.Value}\n" +
                   $"Assembly {virtualDocument.Origin.Assembly.Value}\n" +
                   $"Compilation {virtualDocument.Origin.Compilation.Value}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 8),
        };
        AutomationProperties.SetName(identity, "Virtual source identity");
        Grid content = new() { RowDefinitions = new("Auto,*") };
        content.Children.Add(identity);
        Grid.SetRow(editor, 1);
        content.Children.Add(editor);

        SourceDockDocument document = new()
        {
            Id = id,
            Title = $"{virtualDocument.Title.Value} · read-only",
            Factory = factory,
            CanClose = true,
            CanFloat = true,
            CloseRequested = () => true,
        };
        WorkbenchDockContent.Attach(document, content);
        virtualDocuments.Add(id, editor);
        documents.AddDocument(document);
        SetActiveDocument(document);
        if (virtualDocument.SelectionRange is { } range)
        {
            editor.TextArea.Caret.Line = range.Start.Line + 1;
            editor.TextArea.Caret.Column = range.Start.Character + 1;
            editor.ScrollTo(range.Start.Line + 1, range.Start.Character + 1);
        }
        editor.Focus();
        source.SetStatus($"Opened read-only {VirtualKindLabel(virtualDocument.Kind).ToLowerInvariant()} " +
                         $"for {destination.Display.Value}.");
    }

    private async ValueTask ShowInspectionAsync(
        SourceDocumentSession source,
        WorkbenchCodeInspectionKind kind)
    {
        if (!DocumentIntelligence.CanUse(source)) return;
        source.CloseInteractiveWindows();
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            source.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? sessionId = await documentIntelligence.EnsureSessionAsync(source, token);
            if (sessionId is null || !source.IsCurrentInteraction(version)) return;
            source.SetStatus($"Building {InspectionKindLabel(kind).ToLowerInvariant()} from the exact buffer…");
            WorkbenchCodeInspectionView result = await codeIntelligenceService.InspectAsync(new(
                DocumentIntelligence.Snapshot(source, sessionId, version), kind), token);
            if (!source.IsCurrentInteraction(version)) return;
            if (result.Text is null || result.Title is null || result.Origin is null)
            {
                source.SetStatus(result.Issues.FirstOrDefault()?.Message.Value ??
                    $"{InspectionKindLabel(kind)} is unavailable.");
                return;
            }

            string id = $"inspection:{source.View.GoalId?.Value ?? "original"}:" +
                        $"{source.View.Path.Value}:{kind}";
            TextEditor editor = CodeEditorView.Create(
                result.Text.Value,
                isReadOnly: true,
                wordWrap: false,
                showLineNumbers: true,
                path: InspectionPath(kind));
            AutomationProperties.SetName(editor,
                $"Read-only {InspectionKindLabel(kind)} for {source.View.Path.Value}");
            OpenOrReplaceDocument(id,
                result.Title.Value + (result.IsTruncated ? " · truncated" : string.Empty) +
                " · read-only", editor);
            editor.Focus();
            source.SetStatus($"Opened {InspectionKindLabel(kind).ToLowerInvariant()} · " +
                             $"compilation {result.Origin.Compilation.Value[..12]}…" +
                             (result.IsTruncated ? " · bounded result" : string.Empty));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static string InspectionKindLabel(WorkbenchCodeInspectionKind kind) => kind switch
    {
        WorkbenchCodeInspectionKind.SyntaxTree => "Syntax tree",
        WorkbenchCodeInspectionKind.Symbol => "Symbol details",
        WorkbenchCodeInspectionKind.GeneratedSource => "Generated source",
        WorkbenchCodeInspectionKind.IntermediateLanguage => "Intermediate Language",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string InspectionPath(WorkbenchCodeInspectionKind kind) => kind switch
    {
        WorkbenchCodeInspectionKind.GeneratedSource => "generated.cs",
        WorkbenchCodeInspectionKind.IntermediateLanguage => "inspection.il",
        _ => "inspection.txt",
    };

    private static string VirtualKindLabel(WorkbenchCodeVirtualDocumentKind? kind) => kind switch
    {
        WorkbenchCodeVirtualDocumentKind.GeneratedSource => "Generated source",
        WorkbenchCodeVirtualDocumentKind.MetadataSignature => "Metadata signature",
        WorkbenchCodeVirtualDocumentKind.DecompiledSource => "Decompiled source",
        _ => "Virtual source",
    };

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
                    documentIntelligence.ScheduleDiagnostics(session, immediate: true);
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
                filesTool.ReportStatus(
                    $"{session.View.Path.Value} no longer exists; the stale document was closed.");
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
            RefreshActivatedSourceDocument(next);
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
            problemsToolUnit.Remove(id);
            session.Dispose();
        }
        if (dockable?.Id is { } virtualId)
            virtualDocuments.Remove(virtualId);

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
            RefreshActivatedSourceDocument(document);
        }
        finally
        {
            suppressDocumentActivation = false;
        }
    }

    internal void ReactivateDocumentForTest(IDockable document) => SetActiveDocument(document);

    private void RefreshActivatedSourceDocument(IDockable? document)
    {
        if (document?.Id is { } id &&
            sourceDocuments.TryGetValue(id, out SourceDocumentSession? session) &&
            (!session.Surface.HasDocumentPresentation ||
             !session.Surface.HasCodeLensActions))
        {
            documentIntelligence.SchedulePresentation(session, immediate: true);
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

    private sealed record SymbolDestinationChoice(
        WorkbenchCodeSymbolDestination Destination)
    {
        public override string ToString()
        {
            int line = Destination.Range?.Start.Line + 1 ?? 0;
            string location = Destination.VirtualDocumentId is not null
                ? Destination.Kind is WorkbenchCodeDestinationKind.Generated
                    ? "generated source" : "metadata source"
                : $"{Destination.Path?.Value}:{line}";
            return $"{location}  {Destination.Display.Value}";
        }
    }

    private sealed class UiLoadProgress(TextBlock status) : IProgress<WorkbenchCodeLoadProgress>
    {
        public void Report(WorkbenchCodeLoadProgress value) => Dispatcher.UIThread.Post(() =>
            status.Text = $"{value.Stage} · {value.Message.Value}");
    }

}
