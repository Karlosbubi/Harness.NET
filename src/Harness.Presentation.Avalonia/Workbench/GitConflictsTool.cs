using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class GitConflictsTool
{
    private readonly WorkbenchToolContext context;
    private readonly IWorkbenchCodeIntelligenceService codeIntelligenceService;
    private readonly Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState;
    private readonly Func<WorkbenchWorkspaceContext, DeveloperGitPath, bool> hasOpenSourceDocument;
    private readonly ListBox conflicts = new();
    private readonly TextEditor conflictBase = Editor("conflict-base.cs", true);
    private readonly TextEditor ours = Editor("conflict-ours.cs", true);
    private readonly TextEditor theirs = Editor("conflict-theirs.cs", true);
    private readonly TextEditor result = Editor("conflict-result.cs", false);
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock diagnostics = new() { TextWrapping = TextWrapping.Wrap };
    private DeveloperGitConflictInspectionResult? currentInspection;
    private DeveloperGitConflictDocumentResult? currentDocument;
    private bool rendering;
    private CancellationTokenSource? diagnosticsCancellation;
    private long diagnosticsVersion;
    private readonly SemaphoreSlim codeSessionGate = new(1, 1);
    private WorkbenchCodeSessionId? codeSessionId;
    private string? codeSessionKey;

    internal GitConflictsTool(
        WorkbenchToolContext context,
        IWorkbenchCodeIntelligenceService codeIntelligenceService,
        Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState,
        Func<WorkbenchWorkspaceContext, DeveloperGitPath, bool> hasOpenSourceDocument)
    {
        this.context = context;
        this.codeIntelligenceService = codeIntelligenceService;
        this.renderGitState = renderGitState;
        this.hasOpenSourceDocument = hasOpenSourceDocument;
        Content = BuildContent();
    }

    internal Control Content { get; }
    internal TextBlock Status => status;
    internal bool IsDirty => currentDocument?.Document is { } document && result.Text != document.Result;
    internal bool HasActiveDocument(string path, GoalId? goalId) =>
        currentDocument?.Document is { } document &&
        document.Path.Value.Equals(path, StringComparison.Ordinal) &&
        currentDocument.Context.GoalId == goalId;

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        if (context.IsBusy() || active is null || context.DeveloperGitService is null) return;
        if (!await ResolveUnsavedAsync(WorkbenchDocumentTransition.Reload)) return;
        await context.RunAsync(() => RefreshCoreAsync(active));
    }

    internal async ValueTask RefreshCoreAsync(WorkspaceView active)
    {
        IDeveloperGitService service = context.DeveloperGitService!;
        DeveloperGitConflictInspectionResult inspected = await service.InspectConflictsAsync(
            context.Request(active), context.CancellationToken);
        currentInspection = inspected;
        conflicts.ItemsSource = inspected.Conflicts.Select(item => new ConflictChoice(item)).ToArray();
        conflicts.SelectedIndex = inspected.Conflicts.Count > 0 ? 0 : -1;
        status.Message = inspected.Error ?? (inspected.Conflicts.Count == 0
            ? "No unresolved Git conflicts in this source context."
            : $"{inspected.Conflicts.Count} unresolved path(s)" +
              (inspected.IsTruncated ? " · list truncated" : string.Empty));
        if (inspected.Conflicts.FirstOrDefault() is not { } first)
        {
            currentDocument = null;
            Clear();
            return;
        }
        DeveloperGitConflictDocumentResult document = await service.InspectConflictAsync(
            context.Request(active), first.Path, context.CancellationToken);
        if (hasOpenSourceDocument(document.Context, first.Path))
            status.Message = $"Close the source editor for {first.Path.Value} before opening its merge result; " +
                          "Harness keeps one semantic buffer per path.";
        else
            Render(document);
    }

    internal async ValueTask SaveAsync()
    {
        IDeveloperGitService? service = context.DeveloperGitService;
        if (rendering || context.IsBusy() || service is null || currentDocument?.Document is not { } document ||
            currentDocument.State is null || result.IsReadOnly)
        {
            status.Message = "Select an editable text conflict first.";
            return;
        }
        WorkspaceView? active = context.ActiveWorkspace();
        if (active is null) return;
        await context.RunAsync(async () => Render(await service.SaveConflictResultAsync(new(
            context.Request(active), new(currentDocument.State.Fingerprint), document.Path,
            document.ResultHash, result.Text), context.CancellationToken)));
    }

    internal async ValueTask StageAsync()
    {
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || service is null || currentDocument?.Document is not { } document ||
            currentDocument.State is null || result.Text != document.Result)
        {
            status.Message = "Save the exact current merge result before staging it.";
            return;
        }
        if (document.UnresolvedRegions.Count > 0)
        {
            status.Message = "Remove every displayed conflict-marker region and save again before staging.";
            return;
        }
        WorkspaceView? active = context.ActiveWorkspace();
        if (active is null) return;
        await context.RunAsync(async () =>
        {
            DeveloperGitIndexCommandResult staged = await service.StageConflictResultAsync(new(
                context.Request(active), new(currentDocument.State.Fingerprint), document.Path,
                document.ResultHash), context.CancellationToken);
            if (staged.State is not null) renderGitState(staged.Context, staged.State);
            if (staged.Error is not null)
            {
                status.Message = staged.Error;
                return;
            }
            currentDocument = null;
            Clear();
            await RefreshCoreAsync(active);
            status.Message = $"Staged exact saved result for {document.Path.Value}. " +
                          $"{currentInspection?.Conflicts.Count ?? 0} unresolved path(s) remain.";
        });
    }

    internal async ValueTask<bool> ResolveUnsavedAsync(WorkbenchDocumentTransition transition)
    {
        if (!IsDirty || currentDocument?.Document is not { } document) return true;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (prompt is null) return false;
        WorkbenchUnsavedDecision decision = await prompt.DecideUnsavedAsync(
            new($"Merge result · {document.Path.Value}", transition), context.OwnerWindow());
        if (decision is WorkbenchUnsavedDecision.Cancel) return false;
        if (decision is WorkbenchUnsavedDecision.Save)
        {
            await SaveAsync();
            return !IsDirty;
        }
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (active is null || service is null) return false;
        DeveloperGitConflictDocumentResult refreshed = await service.InspectConflictAsync(
            context.Request(active), document.Path, context.CancellationToken);
        Render(refreshed);
        return refreshed.Document is not null;
    }

    internal void Clear()
    {
        CancelDiagnostics();
        currentInspection = null;
        currentDocument = null;
        conflicts.ItemsSource = Array.Empty<ConflictChoice>();
        rendering = true;
        conflictBase.Text = string.Empty;
        ours.Text = string.Empty;
        theirs.Text = string.Empty;
        result.Text = string.Empty;
        result.IsReadOnly = true;
        diagnostics.Text = string.Empty;
        rendering = false;
    }

    private void Render(DeveloperGitConflictDocumentResult inspected)
    {
        currentDocument = inspected;
        rendering = true;
        try
        {
            if (inspected.Document is not { } document)
            {
                Clear();
                status.Message = inspected.Error ?? "The selected conflict is unavailable.";
                return;
            }
            conflictBase.Text = SideText(document.Base, "base");
            ours.Text = SideText(document.Ours, "ours");
            theirs.Text = SideText(document.Theirs, "theirs");
            result.Text = document.Result;
            result.IsReadOnly = document.ResultIsTruncated ||
                document.Base.IsBinary || document.Ours.IsBinary || document.Theirs.IsBinary;
            status.Message = StateText(document, false);
            diagnostics.Text = document.Path.Value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? "Checking the current merge result with Roslyn…"
                : "Compiler diagnostics do not apply to this file type.";
            ScheduleDiagnostics(document, immediate: true);
        }
        finally
        {
            rendering = false;
        }
    }

    private async ValueTask LoadSelectedAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null ||
            conflicts.SelectedItem is not ConflictChoice selected) return;
        if (currentDocument?.Document is { } current && current.Path != selected.Conflict.Path &&
            !await ResolveUnsavedAsync(WorkbenchDocumentTransition.Switch)) return;
        await context.RunAsync(async () =>
        {
            DeveloperGitConflictDocumentResult inspected = await service.InspectConflictAsync(
                context.Request(active), selected.Conflict.Path, context.CancellationToken);
            if (hasOpenSourceDocument(inspected.Context, selected.Conflict.Path))
                status.Message = $"Close the source editor for {selected.Conflict.Path.Value} before opening its " +
                              "merge result; Harness keeps one semantic buffer per path.";
            else Render(inspected);
        });
    }

    private void UseSide(DeveloperGitConflictSideView side, string label)
    {
        if (side.IsMissing || side.IsBinary || side.IsTruncated || side.Text is null || result.IsReadOnly)
        {
            status.Message = $"The {label} side is not editable text and cannot replace the result here.";
            return;
        }
        result.Text = side.Text;
        result.Focus();
    }

    internal void UseBase() => UseSelectedSide(document => document.Base, "base");

    internal void UseOurs() => UseSelectedSide(document => document.Ours, "ours");

    internal void UseTheirs() => UseSelectedSide(document => document.Theirs, "theirs");

    private void UseSelectedSide(
        Func<DeveloperGitConflictDocumentView, DeveloperGitConflictSideView> select,
        string label)
    {
        if (currentDocument?.Document is { } document) UseSide(select(document), label);
        else status.Message = "Select a current Git conflict first.";
    }

    private Control BuildContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = Button("Refresh conflicts", "Refresh unresolved Git conflicts");
        Button save = Button("Save result", "Save exact merge result without resolving Git index conflict");
        Button stage = Button(
            "Stage saved result",
            "Stage exact saved merge result and resolve selected Git index conflict");
        Button useBase = Button("Use base", "Replace merge result with text from base");
        Button useOurs = Button("Use ours", "Replace merge result with text from ours");
        Button useTheirs = Button("Use theirs", "Replace merge result with text from theirs");
        refresh.Click += async (_, _) => await RefreshAsync();
        save.Click += async (_, _) => await SaveAsync();
        stage.Click += async (_, _) => await StageAsync();
        useBase.Click += (_, _) => UseBase();
        useOurs.Click += (_, _) => UseOurs();
        useTheirs.Click += (_, _) => UseTheirs();
        foreach (Button item in new[] { refresh, save, stage, useBase, useOurs, useTheirs })
            actions.Children.Add(item);
        AutomationProperties.SetName(conflicts, "Unresolved Git conflict paths");
        AutomationProperties.SetName(conflictBase, "Read-only Git conflict base");
        AutomationProperties.SetName(ours, "Read-only Git conflict ours");
        AutomationProperties.SetName(theirs, "Read-only Git conflict theirs");
        AutomationProperties.SetName(result, "Editable Git conflict result");
        AutomationProperties.SetName(status, "Git conflict exact save state");
        AutomationProperties.SetName(diagnostics, "Git conflict result diagnostics");
        conflicts.SelectionChanged += async (_, _) => await LoadSelectedAsync();
        result.TextChanged += (_, _) =>
        {
            if (!rendering && currentDocument?.Document is { } document)
            {
                status.Message = StateText(document, result.Text != document.Result);
                ScheduleDiagnostics(document);
            }
        };
        Grid sides = new() { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 8 };
        AddSide(sides, conflictBase, "Base", 0);
        AddSide(sides, ours, "Ours", 1);
        AddSide(sides, theirs, "Theirs", 2);
        Grid resultPanel = SidePanel("Result", result);
        Grid panel = new() { RowDefinitions = new("Auto,Auto,120,2*,2*,Auto"), RowSpacing = 8 };
        panel.Children.Add(status);
        AddRow(panel, actions, 1);
        AddRow(panel, conflicts, 2);
        AddRow(panel, sides, 3);
        AddRow(panel, resultPanel, 4);
        AddRow(panel, diagnostics, 5);
        return panel;
    }

    private static Button Button(string content, string name)
    {
        Button button = new() { Content = content, Margin = new(0, 0, 6, 6) };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static void AddSide(Grid owner, TextEditor editor, string label, int column)
    {
        Grid panel = SidePanel(label, editor);
        Grid.SetColumn(panel, column);
        owner.Children.Add(panel);
    }

    private static Grid SidePanel(string label, Control editor)
    {
        Grid panel = new() { RowDefinitions = new("Auto,*"), RowSpacing = 4 };
        panel.Children.Add(new TextBlock { Text = label });
        AddRow(panel, editor, 1);
        return panel;
    }

    private static void AddRow(Grid owner, Control child, int row)
    {
        Grid.SetRow(child, row);
        owner.Children.Add(child);
    }

    private static TextEditor Editor(string path, bool readOnly) => CodeEditorView.Create(
        string.Empty, readOnly, wordWrap: false, showLineNumbers: true, path: path);

    private static string SideText(DeveloperGitConflictSideView side, string label) =>
        side.IsMissing ? $"[{label} side does not contain this path]" :
        side.IsBinary ? $"[{label} side is binary · blob {side.Blob?.Value}]" :
        side.IsTruncated ? $"[{label} side exceeds the 1 MiB editor limit]" : side.Text ?? string.Empty;

    private static string StateText(DeveloperGitConflictDocumentView document, bool dirty) =>
        $"{document.Path.Value} · {(dirty ? "unsaved result" : $"saved {document.ResultHash.Value}")} · " +
        $"{document.UnresolvedRegions.Count} unresolved marker region(s). " +
        "Saving does not resolve the index; stage the exact saved result separately.";

    private void ScheduleDiagnostics(
        DeveloperGitConflictDocumentView document,
        bool immediate = false)
    {
        CancelDiagnostics();
        if (!document.Path.Value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            result.IsReadOnly)
        {
            return;
        }

        diagnosticsCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        long version = Volatile.Read(ref diagnosticsVersion);
        _ = SynchronizeDiagnosticsAsync(
            document, result.Text, version, diagnosticsCancellation.Token, immediate);
    }

    private async Task SynchronizeDiagnosticsAsync(
        DeveloperGitConflictDocumentView document,
        string text,
        long version,
        CancellationToken token,
        bool immediate)
    {
        try
        {
            if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            WorkspaceView? active = context.ActiveWorkspace();
            DeveloperGitConflictDocumentResult? selected = currentDocument;
            if (active is null || selected?.Document?.Path != document.Path ||
                version != diagnosticsVersion) return;
            WorkbenchCodeSessionId? sessionId = await EnsureCodeSessionAsync(
                active, selected.Context, token);
            if (sessionId is null || version != diagnosticsVersion) return;
            WorkbenchCodeDiagnosticView synchronized = await codeIntelligenceService.SynchronizeAsync(new(
                sessionId,
                new(document.Path.Value),
                new(document.ResultHash.Value),
                new(version),
                new(text)), token);
            if (version != diagnosticsVersion ||
                synchronized.State is WorkbenchCodeResultState.Cancelled or
                    WorkbenchCodeResultState.Stale) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != diagnosticsVersion) return;
                if (synchronized.Diagnostics.Count == 0)
                {
                    diagnostics.Text = synchronized.Issues.Count == 0
                        ? "Roslyn: no diagnostics in the current merge result."
                        : $"Roslyn unavailable · {synchronized.Issues[0].Message.Value}";
                    return;
                }
                diagnostics.Text = "Roslyn diagnostics:\n" + string.Join('\n',
                    synchronized.Diagnostics.Take(100).Select(item =>
                        $"{item.Severity} {item.Id.Value} " +
                        $"({item.Range.Start.Line + 1},{item.Range.Start.Character + 1}): " +
                        item.Message.Value)) +
                    (synchronized.Diagnostics.Count > 100
                        ? "\n[diagnostics truncated]"
                        : string.Empty);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            if (token.IsCancellationRequested || version != Volatile.Read(ref diagnosticsVersion))
                return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == Volatile.Read(ref diagnosticsVersion))
                    diagnostics.Text = $"Roslyn diagnostics failed · {exception.Message}";
            });
        }
    }

    private void CancelDiagnostics()
    {
        diagnosticsCancellation?.Cancel();
        diagnosticsCancellation?.Dispose();
        diagnosticsCancellation = null;
        Interlocked.Increment(ref diagnosticsVersion);
    }

    private async ValueTask<WorkbenchCodeSessionId?> EnsureCodeSessionAsync(
        WorkspaceView active,
        WorkbenchWorkspaceContext selected,
        CancellationToken requestCancellation)
    {
        string key = $"{active.Id}:{selected.GoalId?.Value ?? "original"}:" +
                     $"{selected.Branch?.Value ?? active.Branch}:{active.EntryPoint}";
        await codeSessionGate.WaitAsync(requestCancellation);
        try
        {
            if (codeSessionId is not null &&
                string.Equals(codeSessionKey, key, StringComparison.Ordinal))
                return codeSessionId;
            if (codeSessionId is not null)
                await codeIntelligenceService.StopAsync(codeSessionId, requestCancellation);
            codeSessionId = null;
            codeSessionKey = null;
            string entryPoint = Path.IsPathRooted(active.EntryPoint)
                ? Path.GetRelativePath(active.RootPath, active.EntryPoint)
                : active.EntryPoint;
            if (entryPoint == ".." ||
                entryPoint.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                return null;
            WorkbenchCodeSessionView started = await codeIntelligenceService.StartAsync(
                new(new(active.Id), selected.GoalId, new(entryPoint)),
                new UiLoadProgress(diagnostics),
                requestCancellation);
            codeSessionId = started.SessionId;
            codeSessionKey = started.SessionId is null ? null : key;
            return started.SessionId;
        }
        finally
        {
            codeSessionGate.Release();
        }
    }

    internal async ValueTask InvalidateCodeIntelligenceAsync()
    {
        CancelDiagnostics();
        bool entered = false;
        try
        {
            await codeSessionGate.WaitAsync(context.CancellationToken);
            entered = true;
            if (codeSessionId is not null)
                await codeIntelligenceService.StopAsync(codeSessionId, context.CancellationToken);
            codeSessionId = null;
            codeSessionKey = null;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (entered) codeSessionGate.Release();
        }
    }

    private sealed record ConflictChoice(DeveloperGitConflictSummaryView Conflict)
    {
        public override string ToString() =>
            $"{(Conflict.IsBinary ? "BINARY" : "TEXT")} · {Conflict.Path.Value} · " +
            $"base {Short(Conflict.BaseBlob)} · ours {Short(Conflict.OursBlob)} · " +
            $"theirs {Short(Conflict.TheirsBlob)}";

        private static string Short(DeveloperGitCommitSha? value) => value is null
            ? "missing"
            : value.Value[..Math.Min(8, value.Value.Length)];
    }

    private sealed class UiLoadProgress(TextBlock target) : IProgress<WorkbenchCodeLoadProgress>
    {
        public void Report(WorkbenchCodeLoadProgress value) => Dispatcher.UIThread.Post(() =>
            target.Text = $"{value.Stage} · {value.Message.Value}");
    }
}
