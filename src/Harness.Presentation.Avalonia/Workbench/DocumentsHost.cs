using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Dock.Model;
using Dock.Model.Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Coverage;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed partial class DocumentsHost
{
    private readonly IWorkbenchDocumentService documentService;
    private readonly IWorkbenchDocumentPrompt prompt;
    private readonly Func<AvaloniaShellState> state;
    private readonly Func<bool> isBusy;
    private readonly Func<Func<ValueTask>, ValueTask> run;
    private readonly Action<string> reportStatus;
    private readonly Func<string?> statusText;
    private readonly Func<string, GoalId?, bool> hasConflictDocument;
    private readonly Func<Window?> ownerWindow;
    private readonly CancellationToken cancellationToken;
    private readonly Factory factory;
    private readonly bool canMutate;
    private readonly Dictionary<string, SourceDocumentSession> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextEditor> virtuals = new(StringComparer.Ordinal);
    private readonly ComboBox switcher = new()
    {
        MinWidth = 170,
        MaxWidth = 260,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly DocumentIntelligence intelligence;
    private readonly DocumentNavigation navigation;
    private readonly DocumentInteractions interactions;
    private readonly DocumentRename rename;
    private readonly DocumentTransformations transformations;
    private readonly DocumentSessionFactory sessionFactory;
    private IDocumentDock documents = null!;
    private IDockable overview = null!;
    private IDockable? active;
    private KeybindingSettingsSnapshot keybindings = KeybindingSettingsSnapshot.Default;
    private bool suppressActivation;
    private bool renderingSwitcher;
    private bool resolvingTransition;

    internal DocumentsHost(
        IWorkbenchDocumentService documentService,
        IWorkbenchCodeIntelligenceService codeService,
        IWorkspaceMutationService? mutationService,
        IWorkbenchDocumentPrompt prompt,
        IDeveloperProjectExecutionService? executionService,
        Func<AvaloniaShellState> state,
        Func<bool> isBusy,
        Func<Func<ValueTask>, ValueTask> run,
        Action<string> reportStatus,
        Func<string?> statusText,
        Func<string, GoalId?, bool> hasConflictDocument,
        Func<Window?> ownerWindow,
        Func<ValueTask> invalidateAll,
        Func<bool> showRunOutput,
        Func<ValueTask> refreshRunOutput,
        IDeveloperDebuggerService? debuggerService,
        Func<DeveloperDebugSessionView, ValueTask> showDebugger,
        Factory factory,
        CancellationToken cancellationToken)
    {
        this.documentService = documentService;
        this.prompt = prompt;
        this.state = state;
        this.isBusy = isBusy;
        this.run = run;
        this.reportStatus = reportStatus;
        this.statusText = statusText;
        this.hasConflictDocument = hasConflictDocument;
        this.ownerWindow = ownerWindow;
        this.factory = factory;
        canMutate = mutationService is not null;
        this.cancellationToken = cancellationToken;
        Problems = new(NavigateToProblemAsync);
        intelligence = new(codeService, executionService, ActiveWorkspace, () => sources,
            Problems, cancellationToken);
        navigation = new(
            codeService, intelligence, executionService, debuggerService, ActiveWorkspace, Request,
            () => sources, virtuals, OpenAsync, SetActive, PrepareActiveTransitionAsync,
            OpenOrReplace, () => documents, factory, showRunOutput, refreshRunOutput,
            showDebugger, cancellationToken);
        interactions = new(codeService, intelligence, ownerWindow,
            navigation.NavigateToSymbolAsync, cancellationToken);
        rename = new(mutationService, ActiveSource, () => sources, ownerWindow,
            invalidateAll, intelligence.ScheduleDiagnostics, cancellationToken);
        transformations = new(mutationService, codeService, intelligence, interactions,
            () => sources, invalidateAll, cancellationToken);
        sessionFactory = new(
            factory,
            () => keybindings,
            intelligence,
            navigation,
            interactions,
            rename,
            transformations,
            document => SaveAsync(document),
            ReloadAsync,
            RequestCloseAsync,
            OnCloseRequested,
            cancellationToken);
    }

    internal ProblemsTool Problems { get; }
    internal ComboBox Switcher => switcher;
    internal IDocumentDock Dock => documents;
    internal IDockable? ActiveDocument => active;
    internal int SourceCount => sources.Count;
    internal int VirtualCount => virtuals.Count;
    internal TextEditor? ActiveSourceEditor => ActiveSource()?.NativeEditor;
    internal TextEditor? ActiveVirtualEditor => active?.Id is { } id &&
        virtuals.TryGetValue(id, out TextEditor? editor) ? editor : active?.Context as TextEditor;
    internal bool ActiveSourceIsDirty => ActiveSource()?.IsDirty == true;
    internal int ActiveCompletionItemCount =>
        ActiveSource()?.CompletionWindow?.CompletionList.CompletionData.Count ?? 0;
    internal CompletionWindow? ActiveCompletionWindow => ActiveSource()?.CompletionWindow;
    internal bool ActiveQuickInfoIsOpen => ActiveSource()?.QuickInfoWindow?.IsVisible is true;
    internal IReadOnlyList<InboundOpenDocumentView> InboundOpenDocuments => sources.Values
        .Select(document => new InboundOpenDocumentView(
            document.View.Path.Value,
            document.View.GoalId?.Value,
            document.View.Sha256?.Value,
            document.CurrentBufferVersion,
            document.IsDirty,
            document.View.Access is WorkbenchDocumentAccess.Editable,
            ReferenceEquals(active, document.Document)))
        .ToArray();

    internal void Attach(IDocumentDock dock, IDockable overviewDocument)
    {
        documents = dock;
        overview = overviewDocument;
        active = dock.ActiveDockable ?? overviewDocument;
        factory.ActiveDockableChanged += OnActiveDockableChanged;
        factory.DockableClosed += OnDockableClosed;
        UpdateSwitcher();
    }

    internal void ReplaceDock(IDocumentDock dock, IDockable overviewDocument)
    {
        documents = dock;
        overview = overviewDocument;
        active = dock.ActiveDockable ?? overviewDocument;
        UpdateSwitcher();
    }

    internal Control BuildActions(
        TextBlock layoutStatus,
        Action<Control> focusRequested,
        Action<IDockable> focusDocument)
    {
        AutomationProperties.SetName(switcher, "Open editor documents");
        switcher.SelectionChanged += async (_, _) =>
        {
            if (renderingSwitcher || switcher.SelectedItem is not DocumentChoice choice) return;
            if (!await TrySwitchAsync(choice.Document)) UpdateSwitcher();
        };
        Button focus = new() { Content = "Focus editor" };
        AutomationProperties.SetName(focus, "Focus the active editor document");
        focus.Click += (_, _) => FocusActive(focusRequested, focusDocument);
        StackPanel actions = new()
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Document", VerticalAlignment = VerticalAlignment.Center },
                switcher,
                focus,
                layoutStatus,
            },
        };
        AutomationProperties.SetName(actions, "Editor document navigation");
        return actions;
    }

    internal void Update(AvaloniaShellState snapshot)
    {
        intelligence.UpdatePreferences(snapshot.Settings.EditorIntelligenceSettings?.Preferences ??
                                       EditorIntelligencePreferences.Default);
        KeybindingSettingsSnapshot next = snapshot.Settings.KeybindingSettings ??
                                          KeybindingSettingsSnapshot.Default;
        bool changed = next != keybindings;
        keybindings = next;
        foreach (SourceDocumentSession document in sources.Values)
        {
            document.Editor.ApplyTheme();
            if (!changed) continue;
            document.Surface.ApplyKeybindings(keybindings);
            document.Vim.SetInputMode(keybindings.InputMode);
        }
    }

    internal ValueTask OpenAsync(string path) => OpenAsync(path, state().Goals.SelectedGoal?.Id);

    internal async ValueTask NavigateToTestAsync(
        WorkbenchCodeTestCase test,
        GoalId? goalId)
    {
        ArgumentNullException.ThrowIfNull(test);
        await OpenAsync(test.Path.Value, goalId);
        SourceDocumentSession? target = sources.Values.FirstOrDefault(item =>
            item.View.GoalId == goalId && item.View.Path.Value == test.Path.Value);
        if (target is null) return;
        SetActive(target.Document);
        target.Editor.SetCaretPosition(test.Range.Start);
        target.Editor.ScrollTo(test.Range.Start);
        target.Editor.Focus();
    }

    internal async ValueTask NavigateToCoverageAsync(
        DeveloperCoverageLine line,
        GoalId? goalId)
    {
        ArgumentNullException.ThrowIfNull(line);
        await OpenAsync(line.Path.Value, goalId);
        SourceDocumentSession? target = sources.Values.FirstOrDefault(item =>
            item.View.GoalId == goalId && item.View.Path.Value == line.Path.Value);
        if (target is null) return;
        SetActive(target.Document);
        WorkbenchCodePosition position = CoveragePosition(line);
        target.Editor.SetCaretPosition(position);
        target.Editor.ScrollTo(position);
        target.Editor.Focus();
    }

    internal static WorkbenchCodePosition CoveragePosition(DeveloperCoverageLine line) =>
        new(Math.Max(0, line.Line.Value - 1), 0);

    internal async ValueTask OpenAsync(string path, GoalId? goalId)
    {
        WorkspaceView? workspace = ActiveWorkspace();
        if (isBusy() || workspace is null || !workspace.IsTrusted || string.IsNullOrWhiteSpace(path))
        {
            reportStatus(workspace is null ? "Select a workspace first." : workspace.IsTrusted
                ? "Enter a relative file path."
                : "Trust the workspace before reading files.");
            return;
        }
        path = path.Trim();
        if (hasConflictDocument(path, goalId))
        {
            reportStatus("That path is active in the Git conflict result editor. " +
                         "Save and stage it there before opening a second buffer.");
            return;
        }
        await run(async () =>
        {
            WorkbenchDocumentView view = await documentService.OpenAsync(new(
                new(workspace.Id), goalId, new(path)), cancellationToken);
            if (view.Error is not null)
            {
                reportStatus(view.Error);
                return;
            }
            string id = SourceId(view);
            if (sources.TryGetValue(id, out SourceDocumentSession? existing))
            {
                if (await TrySwitchAsync(existing.Document)) reportStatus($"Activated {view.Path.Value}.");
                return;
            }
            if (!await PrepareActiveTransitionAsync(WorkbenchDocumentTransition.Switch))
            {
                reportStatus($"Kept unsaved changes; {view.Path.Value} was not opened.");
                return;
            }
            SourceDocumentSession created = sessionFactory.Create(id, view);
            sources.Add(id, created);
            documents.AddDocument(created.Document);
            SetActive(created.Document);
            reportStatus($"Opened {view.Path.Value} · {view.Size.Value:N0} bytes · " +
                         view.AccessDescription.TrimEnd('.') + (view.IsTruncated ? " · truncated." : "."));
        });
    }

    internal async ValueTask<InboundUiActionResult> OpenInboundAsync(InboundUiDocumentRequest request)
    {
        GoalId? goal = string.IsNullOrWhiteSpace(request.GoalId) ? null : new(request.GoalId);
        await OpenAsync(request.RelativePath, goal);
        bool opened = sources.Values.Any(item => item.View.GoalId == goal &&
            item.View.Path.Value.Equals(request.RelativePath, StringComparison.Ordinal));
        return opened
            ? new(new("document.open"), true, null, null)
            : new(new("document.open"), false, "document_open_failed", statusText());
    }

    internal bool HasOpen(WorkbenchWorkspaceContext context, DeveloperGitPath path) =>
        sources.Values.Any(document => document.View.WorkspaceId == context.WorkspaceId &&
            document.View.GoalId == context.GoalId &&
            document.View.Path.Value.Equals(path.Value, StringComparison.Ordinal));

    internal bool IsOriginalDirty(string path)
    {
        SourceDocumentSession? document = sources.Values.FirstOrDefault(item =>
            item.View.GoalId is null && item.View.Path.Value.Equals(path, StringComparison.Ordinal));
        document?.SynchronizeDirtyState();
        return document?.IsDirty == true;
    }

    internal bool HasDirtyOriginals()
    {
        foreach (SourceDocumentSession document in sources.Values.Where(item => item.View.GoalId is null))
            document.SynchronizeDirtyState();
        return sources.Values.Any(item => item.View.GoalId is null && item.IsDirty);
    }

    internal async ValueTask ReloadOriginalAsync(string path)
    {
        SourceDocumentSession? document = sources.Values.FirstOrDefault(item =>
            item.View.GoalId is null && item.View.Path.Value.Equals(path, StringComparison.Ordinal));
        if (document is not null) await ReloadAsync(document, confirmDiscard: false);
    }

    internal async ValueTask<bool> PrepareForShutdownAsync()
    {
        foreach (SourceDocumentSession document in sources.Values.Where(item => item.IsDirty).ToArray())
            if (!await ResolveUnsavedAsync(document, WorkbenchDocumentTransition.Exit, true))
                return false;
        await InvalidateAsync();
        return true;
    }

    internal ValueTask<bool> PrepareForWorkspaceChangeAsync() =>
        CloseAllAsync(WorkbenchDocumentTransition.Switch);

    internal ValueTask<bool> SaveActiveAsync() => ActiveSource() is { } document
        ? SaveAsync(document)
        : ValueTask.FromResult(false);

    internal ValueTask CloseActiveAsync() => ActiveSource() is { } document
        ? RequestCloseAsync(document)
        : ValueTask.CompletedTask;

    internal ValueTask TransformActiveAsync(WorkbenchCodeDocumentTransformationKind kind) =>
        ActiveSource() is { } document ? transformations.TransformAsync(document, kind) :
        ValueTask.CompletedTask;

    internal ValueTask InspectActiveAsync(WorkbenchCodeInspectionKind kind) =>
        ActiveSource() is { } document ? navigation.ShowInspectionAsync(document, kind) :
        ValueTask.CompletedTask;

    internal ValueTask ShowActiveQuickFixesAsync() => ActiveSource() is { } document
        ? transformations.ShowQuickFixesAsync(document)
        : ValueTask.CompletedTask;

    internal ValueTask ApplyActiveCodeActionAsync(WorkbenchCodeActionCandidate candidate) =>
        candidate is null
            ? throw new ArgumentNullException(nameof(candidate))
            : ActiveSource() is { } document
            ? transformations.TransformAsync(document,
                WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                codeActionId: candidate.Id,
                codeActionScope: candidate.Scope)
            : ValueTask.CompletedTask;

    internal ValueTask HandleTextEnteredAsync(string? text) => ActiveSource() is { } document
        ? transformations.HandleTextEnteredAsync(document, text)
        : ValueTask.CompletedTask;

    internal ValueTask HandlePasteAsync(WorkbenchCodeRange range) => ActiveSource() is { } document
        ? transformations.HandlePasteAsync(document, range)
        : ValueTask.CompletedTask;

    internal bool CanTransform(WorkbenchCodeDocumentTransformationKind kind) =>
        ActiveSource() is { } document && transformations.CanTransform(document, kind);

    internal bool CanInvoke(KeybindingCommand command) => ActiveSource() is { } document &&
        command switch
        {
            KeybindingCommand.CloseDocument => true,
            KeybindingCommand.SaveDocument => document.View.Access is WorkbenchDocumentAccess.Editable,
            KeybindingCommand.ShowCompletion or KeybindingCommand.ShowQuickInfo or
                KeybindingCommand.GoToDefinition or KeybindingCommand.FindReferences or
                KeybindingCommand.FindImplementations => DocumentIntelligence.CanUse(document),
            KeybindingCommand.RenameSymbol => canMutate && DocumentIntelligence.CanUse(document) &&
                document.View.Access is WorkbenchDocumentAccess.Editable,
            KeybindingCommand.FormatDocument => transformations.CanTransform(document,
                WorkbenchCodeDocumentTransformationKind.FormatDocument),
            KeybindingCommand.FormatSelection => transformations.CanTransform(document,
                WorkbenchCodeDocumentTransformationKind.FormatSelection),
            KeybindingCommand.FormatChangedCode => transformations.CanTransform(document,
                WorkbenchCodeDocumentTransformationKind.FormatChangedSpans),
            KeybindingCommand.OrganizeImports => transformations.CanTransform(document,
                WorkbenchCodeDocumentTransformationKind.OrganizeImports),
            KeybindingCommand.RemoveUnusedImports => transformations.CanTransform(document,
                WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports),
            KeybindingCommand.ShowQuickFixes => transformations.CanTransform(document,
                WorkbenchCodeDocumentTransformationKind.AddMissingImport),
            _ => false,
        };

    internal async ValueTask InvokeActiveAsync(KeybindingCommand command)
    {
        if (ActiveSource() is { } document) await sessionFactory.InvokeAsync(document, command);
    }

    internal ValueTask<PendingWorkbenchRename?> PreviewRenameAsync(string name) =>
        rename.PreviewActiveAsync(name);

    internal ValueTask<RenameSymbolApplyView?> ApplyRenameAsync(PendingWorkbenchRename pending) =>
        rename.ApplyActiveAsync(pending);

    internal void ReactivateForTest(IDockable document) => SetActive(document);

    internal void ReplaceActive(IDockable document) => SetActive(document);

    internal IDockable OpenOrReplace(string id, string title, Control content)
    {
        IDockable? existing = documents.VisibleDockables?.FirstOrDefault(item => item.Id == id);
        if (existing is not null)
        {
            existing.Title = title;
            WorkbenchDockContent.Attach(existing, content);
            SetActive(existing);
            return existing;
        }
        factory.Document(out IDocument? document, item => item
            .WithId(id).WithTitle(title).WithCanClose(true).WithCanFloat(true).WithContext(content));
        IDocument created = document ?? throw new InvalidOperationException(
            "Dock did not create the document.");
        WorkbenchDockContent.Attach(created, content);
        documents.AddDocument(created);
        SetActive(created);
        return created;
    }

    internal async ValueTask<bool> CloseAllAsync(WorkbenchDocumentTransition transition)
    {
        foreach (SourceDocumentSession document in sources.Values.ToArray())
        {
            if (document.IsDirty && !await ResolveUnsavedAsync(document, transition, false))
            {
                SetActive(document.Document);
                return false;
            }
            document.AllowClose = true;
            factory.CloseDockable(document.Document);
        }
        foreach (IDockable document in documents.VisibleDockables?
                     .Where(item => item.Id != WorkbenchDockIds.OverviewDocument).ToArray() ?? [])
            factory.CloseDockable(document);
        SetActive(overview);
        return true;
    }

    internal void ActivateOverview() => SetActive(overview);

    internal async ValueTask InvalidateAsync()
    {
        await intelligence.InvalidateAsync();
    }

    private async ValueTask<bool> SaveAsync(
        SourceDocumentSession document,
        WorkbenchDocumentSha256? overrideBaseline = null)
    {
        if (document.View.Access is not WorkbenchDocumentAccess.Editable || !document.IsDirty)
            return !document.IsDirty;
        document.SetBusy(true, document.View.GoalId is null
            ? "Saving to the active trusted workspace…"
            : "Saving through the approved goal worktree…");
        try
        {
            WorkbenchDocumentSha256? baseline = overrideBaseline ?? document.View.Sha256;
            while (true)
            {
                WorkbenchDocumentSaveResult result = await documentService.SaveAsync(new(
                    document.View.WorkspaceId,
                    document.View.GoalId,
                    NewEditCorrelation(),
                    document.View.Path,
                    baseline,
                    new(document.Editor.Text)), cancellationToken);
                if (result.Outcome is WorkbenchDocumentSaveOutcome.Saved &&
                    result.SavedSha256 is not null)
                {
                    document.AcceptSaved(result.SavedSha256, result.BytesWritten);
                    intelligence.ScheduleDiagnostics(document, true);
                    return true;
                }
                if (result.Outcome is not WorkbenchDocumentSaveOutcome.Conflict)
                {
                    document.SetStatus(result.Error ?? "The source document was not saved.");
                    return false;
                }
                document.SetStatus(result.CurrentSha256 is null
                    ? "Save conflict: the file was deleted in the goal worktree."
                    : "Save conflict: the file changed in the goal worktree.");
                WorkbenchConflictDecision decision = await prompt.DecideConflictAsync(new(
                    document.View.Path.Value, result.CurrentSha256 is null), ownerWindow());
                if (decision is WorkbenchConflictDecision.Reload)
                    return await ReloadAsync(document, false);
                if (decision is WorkbenchConflictDecision.Overwrite)
                {
                    baseline = result.CurrentSha256;
                    continue;
                }
                if (decision is WorkbenchConflictDecision.Cancel) return false;
                throw new ArgumentOutOfRangeException(nameof(decision));
            }
        }
        catch (OperationCanceledException)
        {
            document.SetStatus("Source save cancelled; editor changes are still present.");
            return false;
        }
        catch (Exception exception)
        {
            document.SetStatus($"Source save failed: {exception.Message}");
            return false;
        }
        finally
        {
            document.SetBusy(false);
        }
    }

    private async ValueTask<bool> ReloadAsync(SourceDocumentSession document, bool confirmDiscard)
    {
        if (confirmDiscard && document.IsDirty)
        {
            WorkbenchUnsavedDecision decision = await prompt.DecideUnsavedAsync(new(
                document.View.Path.Value, WorkbenchDocumentTransition.Reload), ownerWindow());
            if (decision is WorkbenchUnsavedDecision.Cancel) return false;
            if (decision is WorkbenchUnsavedDecision.Save) return await SaveAsync(document);
        }
        document.SetBusy(true, "Reloading from the workspace…");
        try
        {
            WorkbenchDocumentView current = await documentService.OpenAsync(new(
                document.View.WorkspaceId, document.View.GoalId, document.View.Path),
                cancellationToken);
            if (current.ErrorCode == "file_missing")
            {
                document.AllowClose = true;
                factory.CloseDockable(document.Document);
                reportStatus($"{document.View.Path.Value} no longer exists; the stale document was closed.");
                return true;
            }
            if (current.Error is not null)
            {
                document.SetStatus($"Reload failed: {current.Error}");
                return false;
            }
            document.ReplaceWith(current);
            return true;
        }
        catch (OperationCanceledException)
        {
            document.SetStatus("Reload cancelled; editor content was kept.");
            return false;
        }
        catch (Exception exception)
        {
            document.SetStatus($"Reload failed: {exception.Message}");
            return false;
        }
        finally
        {
            document.SetBusy(false);
        }
    }

    private async ValueTask RequestCloseAsync(SourceDocumentSession document)
    {
        if (!document.IsDirty || await ResolveUnsavedAsync(
                document, WorkbenchDocumentTransition.Close, false))
        {
            document.AllowClose = true;
            factory.CloseDockable(document.Document);
        }
    }

    private bool OnCloseRequested(SourceDocumentSession document)
    {
        if (!document.IsDirty || document.AllowClose) return true;
        if (resolvingTransition) return false;
        resolvingTransition = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RequestCloseAsync(document);
                if (sources.ContainsKey(document.Document.Id ?? string.Empty))
                {
                    document.IgnoreNextActivationChange = true;
                    SetActive(document.Document);
                }
            }
            finally
            {
                resolvingTransition = false;
            }
        });
        return false;
    }

    private async ValueTask<bool> ResolveUnsavedAsync(
        SourceDocumentSession document,
        WorkbenchDocumentTransition transition,
        bool discardKeepsDocument)
    {
        return await prompt.DecideUnsavedAsync(new(document.View.Path.Value, transition), ownerWindow()) switch
        {
            WorkbenchUnsavedDecision.Save => await SaveAsync(document),
            WorkbenchUnsavedDecision.Discard => Discard(document, discardKeepsDocument),
            WorkbenchUnsavedDecision.Cancel => false,
            _ => throw new InvalidOperationException("Unknown unsaved-document decision."),
        };
    }

    private static bool Discard(SourceDocumentSession document, bool keep)
    {
        if (keep) document.DiscardChanges();
        return true;
    }

    private async ValueTask<bool> PrepareActiveTransitionAsync(WorkbenchDocumentTransition transition)
    {
        SourceDocumentSession? document = ActiveSource();
        return document is null || !document.IsDirty ||
               await ResolveUnsavedAsync(document, transition, true);
    }

    private async ValueTask<bool> TrySwitchAsync(IDockable next)
    {
        if (ReferenceEquals(active, next)) return true;
        if (!await PrepareActiveTransitionAsync(WorkbenchDocumentTransition.Switch)) return false;
        SetActive(next);
        return true;
    }

    private async void OnActiveDockableChanged(
        object? sender,
        Dock.Model.Core.Events.ActiveDockableChangedEventArgs args)
    {
        IDockable? next = args.Dockable;
        if (next is null || suppressActivation || resolvingTransition ||
            !IsDocument(next) || ReferenceEquals(active, next)) return;
        IDockable? previous = active;
        if (previous?.Id is not null && sources.TryGetValue(previous.Id, out var pending) &&
            pending.IgnoreNextActivationChange)
        {
            pending.IgnoreNextActivationChange = false;
            SetActive(previous);
            return;
        }
        if (previous?.Id is null || !sources.TryGetValue(previous.Id, out var document) ||
            !document.IsDirty || document.AllowClose)
        {
            active = next;
            UpdateSwitcher();
            RefreshActivated(next);
            return;
        }
        resolvingTransition = true;
        try
        {
            SetActive(previous);
            if (await ResolveUnsavedAsync(document, WorkbenchDocumentTransition.Switch, true))
                SetActive(next);
        }
        finally
        {
            resolvingTransition = false;
        }
    }

    private void OnDockableClosed(object? sender, Dock.Model.Core.Events.DockableClosedEventArgs args)
    {
        IDockable? dockable = args.Dockable;
        if (dockable?.Id is { } id && sources.Remove(id, out SourceDocumentSession? document))
        {
            Problems.Remove(id);
            document.Dispose();
        }
        if (dockable?.Id is { } virtualId) virtuals.Remove(virtualId);
        if (ReferenceEquals(active, dockable)) active = overview;
        Dispatcher.UIThread.Post(UpdateSwitcher);
    }

    private void SetActive(IDockable document)
    {
        suppressActivation = true;
        try
        {
            factory.SetActiveDockable(document);
            active = document;
            UpdateSwitcher();
            RefreshActivated(document);
        }
        finally
        {
            suppressActivation = false;
        }
    }

    private void RefreshActivated(IDockable? document)
    {
        if (document?.Id is { } id && sources.TryGetValue(id, out var source) &&
            (!source.Surface.HasDocumentPresentation || !source.Surface.HasCodeLensActions))
            intelligence.SchedulePresentation(source, true);
    }

    private void UpdateSwitcher()
    {
        renderingSwitcher = true;
        try
        {
            DocumentChoice[] choices = documents.VisibleDockables?.Where(IsDocument)
                .Select(item => new DocumentChoice(item)).ToArray() ?? [];
            switcher.ItemsSource = choices;
            switcher.SelectedItem = choices.FirstOrDefault(item => ReferenceEquals(item.Document, active));
        }
        finally
        {
            renderingSwitcher = false;
        }
    }

    private void FocusActive(Action<Control> requested, Action<IDockable> focusDocument)
    {
        ownerWindow()?.Activate();
        if ((ActiveSourceEditor ?? ActiveVirtualEditor) is { } editor)
        {
            requested(editor);
            if (!editor.Focus()) Dispatcher.UIThread.Post(() => editor.Focus());
            return;
        }
        if (active is { } document) focusDocument(document);
    }

    private async ValueTask NavigateToProblemAsync(WorkbenchCodeDiagnostic diagnostic, GoalId? goal)
    {
        SourceDocumentSession? target = sources.Values.FirstOrDefault(item =>
            item.View.GoalId == goal && item.View.Path.Value == diagnostic.Path.Value);
        if (target is null)
        {
            await OpenAsync(diagnostic.Path.Value, goal);
            target = sources.Values.FirstOrDefault(item =>
                item.View.GoalId == goal && item.View.Path.Value == diagnostic.Path.Value);
        }
        if (target is null) return;
        SetActive(target.Document);
        target.Editor.SetCaretPosition(diagnostic.Range.Start);
        target.Editor.ScrollTo(diagnostic.Range.Start);
        target.Editor.Focus();
    }

    private SourceDocumentSession? ActiveSource() => active?.Id is { } id &&
        sources.TryGetValue(id, out SourceDocumentSession? document) ? document : null;
    private WorkspaceView? ActiveWorkspace() =>
        state().Workspaces.Registered.FirstOrDefault(item => item.IsActive);
    private WorkbenchWorkspaceRequest Request(WorkspaceView workspace)
    {
        GoalView? goal = state().Goals.SelectedGoal;
        return new(new(workspace.Id), goal?.WorkspaceId == workspace.Id ? goal.Id : null);
    }
    private static bool IsDocument(IDockable dockable) => dockable is IDocument and not ITool;
    private static string SourceId(WorkbenchDocumentView view) =>
        $"document.file.{view.WorkspaceId.Value}.{view.GoalId?.Value ?? "original"}.{view.Path.Value}";
    private static ToolCorrelationId NewEditCorrelation() =>
        new($"desktop-edit-{Guid.NewGuid():N}");
    private sealed record DocumentChoice(IDockable Document)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Document.Title) ? "Untitled document" : Document.Title;
    }
}
