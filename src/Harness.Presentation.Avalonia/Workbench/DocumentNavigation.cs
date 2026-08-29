using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Dock.Model.Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal enum SemanticNavigationKind
{
    Definition,
    References,
    Implementations,
}

internal sealed class DocumentNavigation
{
    private readonly IWorkbenchCodeIntelligenceService code;
    private readonly DocumentIntelligence intelligence;
    private readonly IDeveloperProjectExecutionService? execution;
    private readonly Func<WorkspaceView?> activeWorkspace;
    private readonly Func<WorkspaceView, WorkbenchWorkspaceRequest> request;
    private readonly Func<IReadOnlyDictionary<string, SourceDocumentSession>> sourceDocuments;
    private readonly IDictionary<string, TextEditor> virtualDocuments;
    private readonly Func<string, GoalId?, ValueTask> openFile;
    private readonly Action<IDockable> setActive;
    private readonly Func<WorkbenchDocumentTransition, ValueTask<bool>> prepareTransition;
    private readonly Func<string, string, Control, IDockable> openOrReplace;
    private readonly Func<IDocumentDock> documentDock;
    private readonly IFactory factory;
    private readonly Func<bool> showRunOutput;
    private readonly Func<ValueTask> refreshRunOutput;
    private readonly CancellationToken cancellationToken;

    internal DocumentNavigation(
        IWorkbenchCodeIntelligenceService code,
        DocumentIntelligence intelligence,
        IDeveloperProjectExecutionService? execution,
        Func<WorkspaceView?> activeWorkspace,
        Func<WorkspaceView, WorkbenchWorkspaceRequest> request,
        Func<IReadOnlyDictionary<string, SourceDocumentSession>> sourceDocuments,
        IDictionary<string, TextEditor> virtualDocuments,
        Func<string, GoalId?, ValueTask> openFile,
        Action<IDockable> setActive,
        Func<WorkbenchDocumentTransition, ValueTask<bool>> prepareTransition,
        Func<string, string, Control, IDockable> openOrReplace,
        Func<IDocumentDock> documentDock,
        IFactory factory,
        Func<bool> showRunOutput,
        Func<ValueTask> refreshRunOutput,
        CancellationToken cancellationToken)
    {
        this.code = code;
        this.intelligence = intelligence;
        this.execution = execution;
        this.activeWorkspace = activeWorkspace;
        this.request = request;
        this.sourceDocuments = sourceDocuments;
        this.virtualDocuments = virtualDocuments;
        this.openFile = openFile;
        this.setActive = setActive;
        this.prepareTransition = prepareTransition;
        this.openOrReplace = openOrReplace;
        this.documentDock = documentDock;
        this.factory = factory;
        this.showRunOutput = showRunOutput;
        this.refreshRunOutput = refreshRunOutput;
        this.cancellationToken = cancellationToken;
    }

    internal async ValueTask NavigateAsync(
        SourceDocumentSession document,
        SemanticNavigationKind kind)
    {
        if (!DocumentIntelligence.CanUse(document)) return;
        document.CloseInteractiveWindows();
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null) return;
            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                document, session, version);
            document.SetStatus(kind switch
            {
                SemanticNavigationKind.Definition => "Finding definition with Roslyn…",
                SemanticNavigationKind.References => "Finding usages with Roslyn…",
                SemanticNavigationKind.Implementations => "Finding implementations with Roslyn…",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            });
            WorkbenchCodeNavigationView result = kind switch
            {
                SemanticNavigationKind.Definition => await code.FindDefinitionAsync(snapshot, token),
                SemanticNavigationKind.References => await code.FindReferencesAsync(snapshot, token),
                SemanticNavigationKind.Implementations =>
                    await code.FindImplementationsAsync(snapshot, token),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            if (!document.IsCurrentInteraction(version)) return;
            WorkbenchCodeSymbolDestination[] navigable = result.Destinations.Where(destination =>
                destination.Kind is WorkbenchCodeDestinationKind.Source &&
                    destination.Path is not null && destination.Range is not null ||
                destination.VirtualDocumentId is not null).ToArray();
            if (kind is not SemanticNavigationKind.References && navigable.Length == 1)
            {
                await NavigateToDestinationAsync(navigable[0], document);
                return;
            }
            if (navigable.Length == 0)
            {
                document.SetStatus(result.Destinations.FirstOrDefault()?.Display.Value ??
                    "No editable source destination is available for this symbol.");
                return;
            }
            document.SetStatus($"Found {navigable.Length:N0} navigable {Label(kind)} " +
                               (navigable.Length == 1 ? "destination." : "destinations."));
            ShowDestinations(document, navigable,
                $"{navigable.Length} navigable {Label(kind)} destinations for {document.View.Path.Value}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    internal async ValueTask InvokeCodeLensAsync(
        SourceDocumentSession document,
        WorkbenchCodeLens lens)
    {
        document.Editor.SetCaretPosition(lens.Target);
        switch (lens.Kind)
        {
            case WorkbenchCodeLensKind.References:
                await NavigateAsync(document, SemanticNavigationKind.References);
                break;
            case WorkbenchCodeLensKind.Implementations:
                await NavigateAsync(document, SemanticNavigationKind.Implementations);
                break;
            case WorkbenchCodeLensKind.Tests:
                await ShowAssociatedTestsAsync(document);
                break;
            case WorkbenchCodeLensKind.Run:
                await RunAsync(document, lens);
                break;
            case WorkbenchCodeLensKind.Debug:
                document.SetStatus(execution?.Capabilities.DebugStatus ??
                    "No typed debugger capability is available.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lens));
        }
    }

    private async ValueTask RunAsync(SourceDocumentSession document, WorkbenchCodeLens lens)
    {
        WorkspaceView? workspace = activeWorkspace();
        if (execution is null || workspace is null || !workspace.IsTrusted ||
            lens.ExecutionTarget is null)
        {
            document.SetStatus("No validated project execution target is available.");
            return;
        }
        if (document.IsDirty)
        {
            document.SetStatus("Save this document before running its entry point.");
            return;
        }
        Window? owner = TopLevel.GetTopLevel(document.NativeEditor) as Window;
        if (owner is null)
        {
            document.SetStatus("The one-run override dialog requires an active editor window.");
            return;
        }
        DeveloperRunOverrideDialogResult? selected = await new DeveloperRunOverrideDialog(
            lens.ExecutionTarget.ProjectPath.Value).ShowDialog<DeveloperRunOverrideDialogResult?>(
            owner);
        if (selected is null)
        {
            document.SetStatus("Run cancelled before start; no overrides were retained.");
            return;
        }
        document.SetBusy(true, $"Starting {lens.ExecutionTarget.ProjectPath.Value}…");
        try
        {
            DeveloperExecutionStartResult started = await execution.StartRunAsync(new(
                request(workspace), lens.ExecutionTarget, selected.Overrides, selected.Mode),
                cancellationToken);
            if (started.Execution is null)
            {
                document.SetStatus(started.Error ?? "The project run could not start.");
                return;
            }
            document.SetStatus($"Run {started.Execution.Id.Value[..8]} started for " +
                               $"{started.Execution.Project.ProjectPath.Value} · " +
                               $"{OverrideSummary(selected.Overrides)}.");
            showRunOutput();
            await refreshRunOutput();
            _ = PollRunAsync(started.Execution.Id);
        }
        finally
        {
            document.SetBusy(false);
        }
    }

    internal static string OverrideSummary(DeveloperRunOverrides overrides)
    {
        List<string> values = [];
        if (overrides.LaunchProfile is { } profile)
            values.Add($"profile {profile.Value}");
        if (!overrides.Arguments.IsDefaultOrEmpty)
            values.Add($"{overrides.Arguments.Length} argument(s)");
        if (!overrides.Environment.IsDefaultOrEmpty)
            values.Add("environment " + string.Join(", ",
                overrides.Environment.Select(variable => variable.Name.Value)));
        if (overrides.WorkingDirectory is { } directory)
            values.Add($"working directory {directory.Value}");
        return values.Count == 0 ? "default launch" : string.Join(" · ", values);
    }

    private async Task PollRunAsync(DeveloperExecutionId id)
    {
        if (execution is null) return;
        try
        {
            for (int attempt = 0; attempt < 1200; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                WorkspaceView? workspace = activeWorkspace();
                if (workspace is null) return;
                DeveloperExecutionListResult listed = await execution.ListAsync(
                    request(workspace), cancellationToken);
                DeveloperExecutionView? item = listed.Executions.FirstOrDefault(value =>
                    value.Id == id);
                if (item is null || item.State is not DeveloperExecutionState.Running)
                {
                    await refreshRunOutput();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask ShowAssociatedTestsAsync(SourceDocumentSession document)
    {
        if (!DocumentIntelligence.CanUse(document)) return;
        document.CloseInteractiveWindows();
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null) return;
            document.SetStatus("Finding associated tests with Roslyn…");
            WorkbenchCodeSemanticView result = await code.FindAssociatedTestsAsync(new(
                DocumentIntelligence.Snapshot(document, session, version),
                Query: null,
                MaximumResults: 100,
                Offset: 0), token);
            if (!document.IsCurrentInteraction(version)) return;
            WorkbenchCodeSymbolDestination[] source = result.Items.Select(item => item.Destination)
                .Where(destination => destination.Kind is WorkbenchCodeDestinationKind.Source &&
                    destination.Path is not null && destination.Range is not null).ToArray();
            if (source.Length == 0)
            {
                document.SetStatus("No associated source tests were found for this declaration.");
                return;
            }
            document.SetStatus($"Found {source.Length:N0} associated test" +
                               (source.Length == 1 ? "." : "s."));
            ShowDestinations(document, source,
                $"{source.Length} associated tests for {document.View.Path.Value}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            document.SetStatus($"Associated-test lookup failed · {exception.Message}");
        }
    }

    private void ShowDestinations(
        SourceDocumentSession document,
        IReadOnlyList<WorkbenchCodeSymbolDestination> destinations,
        string automationName)
    {
        ListBox list = new()
        {
            ItemsSource = destinations.Select(item => new DestinationChoice(item)).ToArray(),
            MaxHeight = 320,
            MinWidth = 420,
        };
        AutomationProperties.SetName(list, automationName);
        InsightWindow window = new(document.NativeEditor.TextArea)
        {
            Child = list,
            StartOffset = document.Editor.CaretOffset,
            EndOffset = document.Editor.CaretOffset,
        };
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is DestinationChoice choice)
            {
                window.Hide();
                await NavigateToDestinationAsync(choice.Destination, document);
            }
        };
        document.QuickInfoWindow?.Hide();
        document.QuickInfoWindow = window;
        window.Show();
    }

    internal async ValueTask NavigateToSymbolAsync(
        WorkbenchCodeSymbolDestination destination,
        GoalId? goalId)
    {
        if (destination.Path is null || destination.Range is null) return;
        await openFile(destination.Path.Value, goalId);
        SourceDocumentSession? target = sourceDocuments().Values.FirstOrDefault(value =>
            value.View.GoalId == goalId &&
            value.View.Path.Value.Equals(destination.Path.Value, StringComparison.Ordinal));
        if (target is null) return;
        setActive(target.Document);
        WorkbenchCodePosition position = destination.Range.Start;
        target.Editor.SetCaretPosition(position);
        target.Editor.ScrollTo(position);
        target.Editor.Focus();
    }

    private ValueTask NavigateToDestinationAsync(
        WorkbenchCodeSymbolDestination destination,
        SourceDocumentSession source) => destination.VirtualDocumentId is null
        ? NavigateToSymbolAsync(destination, source.View.GoalId)
        : OpenVirtualAsync(source, destination);

    private async ValueTask OpenVirtualAsync(
        SourceDocumentSession source,
        WorkbenchCodeSymbolDestination destination)
    {
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            source.BeginInteraction(cancellationToken);
        WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(source, token);
        if (session is null || !source.IsCurrentInteraction(version)) return;
        WorkbenchCodeVirtualDocumentView result = await code.GetVirtualDocumentAsync(new(
            DocumentIntelligence.Snapshot(source, session, version),
            destination.VirtualDocumentId!), token);
        if (!source.IsCurrentInteraction(version) || result.Text is null ||
            result.Title is null || result.Origin is null)
        {
            source.SetStatus(result.Issues.FirstOrDefault()?.Message.Value ??
                "The virtual source document is unavailable.");
            return;
        }
        string id = $"virtual:{session.Value}:{result.Id.Value}";
        if (virtualDocuments.TryGetValue(id, out TextEditor? existing))
        {
            IDockable? existingDocument = documentDock().VisibleDockables?
                .FirstOrDefault(item => item.Id == id);
            if (existingDocument is not null) setActive(existingDocument);
            existing.Focus();
            return;
        }
        if (!await prepareTransition(WorkbenchDocumentTransition.Switch)) return;
        TextEditor editor = CodeEditorView.Create(
            result.Text.Value, true, wordWrap: false, showLineNumbers: true, path: "virtual.cs");
        AutomationProperties.SetName(editor,
            $"Read-only {VirtualLabel(result.Kind)} for {result.Title.Value}");
        TextBlock identity = new()
        {
            Text = $"{VirtualLabel(result.Kind)} · read-only · {result.Origin.Project.Value} · " +
                   $"{result.Origin.TargetFramework.Value} · {result.Origin.Configuration.Value}\n" +
                   $"Assembly {result.Origin.Assembly.Value}\n" +
                   $"Compilation {result.Origin.Compilation.Value}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(10, 8),
        };
        AutomationProperties.SetName(identity, "Virtual source identity");
        Grid content = new() { RowDefinitions = new("Auto,*") };
        content.Children.Add(identity);
        Grid.SetRow(editor, 1);
        content.Children.Add(editor);
        SourceDockDocument document = new()
        {
            Id = id,
            Title = $"{result.Title.Value} · read-only",
            Factory = factory,
            CanClose = true,
            CanFloat = true,
            CloseRequested = () => true,
        };
        WorkbenchDockContent.Attach(document, content);
        virtualDocuments.Add(id, editor);
        documentDock().AddDocument(document);
        setActive(document);
        if (result.SelectionRange is { } range)
        {
            editor.TextArea.Caret.Line = range.Start.Line + 1;
            editor.TextArea.Caret.Column = range.Start.Character + 1;
            editor.ScrollTo(range.Start.Line + 1, range.Start.Character + 1);
        }
        editor.Focus();
        source.SetStatus($"Opened read-only {VirtualLabel(result.Kind).ToLowerInvariant()} " +
                         $"for {destination.Display.Value}.");
    }

    internal async ValueTask ShowInspectionAsync(
        SourceDocumentSession source,
        WorkbenchCodeInspectionKind kind)
    {
        if (!DocumentIntelligence.CanUse(source)) return;
        source.CloseInteractiveWindows();
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            source.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(source, token);
            if (session is null || !source.IsCurrentInteraction(version)) return;
            source.SetStatus($"Building {InspectionLabel(kind).ToLowerInvariant()} from the exact buffer…");
            WorkbenchCodeInspectionView result = await code.InspectAsync(new(
                DocumentIntelligence.Snapshot(source, session, version), kind), token);
            if (!source.IsCurrentInteraction(version)) return;
            if (result.Text is null || result.Title is null || result.Origin is null)
            {
                source.SetStatus(result.Issues.FirstOrDefault()?.Message.Value ??
                    $"{InspectionLabel(kind)} is unavailable.");
                return;
            }
            TextEditor editor = CodeEditorView.Create(
                result.Text.Value, true, wordWrap: false, showLineNumbers: true,
                path: InspectionPath(kind));
            AutomationProperties.SetName(editor,
                $"Read-only {InspectionLabel(kind)} for {source.View.Path.Value}");
            openOrReplace(
                $"inspection:{source.View.GoalId?.Value ?? "original"}:{source.View.Path.Value}:{kind}",
                result.Title.Value + (result.IsTruncated ? " · truncated" : string.Empty) +
                " · read-only",
                editor);
            editor.Focus();
            source.SetStatus($"Opened {InspectionLabel(kind).ToLowerInvariant()} · " +
                             $"compilation {result.Origin.Compilation.Value[..12]}…" +
                             (result.IsTruncated ? " · bounded result" : string.Empty));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static string Label(SemanticNavigationKind kind) => kind switch
    {
        SemanticNavigationKind.Definition => "definition",
        SemanticNavigationKind.References => "usage",
        SemanticNavigationKind.Implementations => "implementation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string InspectionLabel(WorkbenchCodeInspectionKind kind) => kind switch
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

    private static string VirtualLabel(WorkbenchCodeVirtualDocumentKind? kind) => kind switch
    {
        WorkbenchCodeVirtualDocumentKind.GeneratedSource => "Generated source",
        WorkbenchCodeVirtualDocumentKind.MetadataSignature => "Metadata signature",
        WorkbenchCodeVirtualDocumentKind.DecompiledSource => "Decompiled source",
        _ => "Virtual source",
    };

    private sealed record DestinationChoice(WorkbenchCodeSymbolDestination Destination)
    {
        public override string ToString()
        {
            int line = Destination.Range?.Start.Line + 1 ?? 0;
            string location = Destination.VirtualDocumentId is not null
                ? Destination.Kind is WorkbenchCodeDestinationKind.Generated
                    ? "generated source"
                    : "metadata source"
                : $"{Destination.Path?.Value}:{line}";
            return $"{location}  {Destination.Display.Value}";
        }
    }
}
