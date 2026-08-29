using Avalonia.Threading;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class DocumentIntelligence
{
    private readonly IWorkbenchCodeIntelligenceService service;
    private readonly IDeveloperProjectExecutionService? executionService;
    private readonly Func<WorkspaceView?> activeWorkspace;
    private readonly Func<IReadOnlyDictionary<string, SourceDocumentSession>> documents;
    private readonly ProblemsTool problems;
    private readonly CancellationToken cancellationToken;
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private WorkbenchCodeSessionId? sessionId;
    private string? sessionKey;
    private EditorIntelligencePreferences preferences = EditorIntelligencePreferences.Default;

    internal DocumentIntelligence(
        IWorkbenchCodeIntelligenceService service,
        IDeveloperProjectExecutionService? executionService,
        Func<WorkspaceView?> activeWorkspace,
        Func<IReadOnlyDictionary<string, SourceDocumentSession>> documents,
        ProblemsTool problems,
        CancellationToken cancellationToken)
    {
        this.service = service;
        this.executionService = executionService;
        this.activeWorkspace = activeWorkspace;
        this.documents = documents;
        this.problems = problems;
        this.cancellationToken = cancellationToken;
    }

    internal EditorIntelligencePreferences Preferences => preferences;

    internal void UpdatePreferences(EditorIntelligencePreferences value)
    {
        if (value == preferences) return;
        preferences = value;
        foreach (SourceDocumentSession document in documents().Values)
            SchedulePresentation(document, immediate: true, includeStructure: false);
    }

    internal void ScheduleDiagnostics(SourceDocumentSession document, bool immediate = false)
    {
        if (document.IsDisposed) return;
        if (document.View.Sha256 is null || document.View.IsTruncated || !CanUse(document))
        {
            document.Surface.SetCodeHealthNotApplicable();
            return;
        }
        document.Surface.BeginCodeHealthUpdate();
        if (document.Document.Id is { } id) problems.Remove(id);
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginDiagnostics(cancellationToken);
        _ = SynchronizeDiagnosticsAsync(document, version, token, immediate);
    }

    internal void SchedulePresentation(
        SourceDocumentSession document,
        bool immediate = false,
        bool includeStructure = true)
    {
        if (document.IsDisposed || !CanUse(document)) return;
        CancellationToken token = document.BeginPresentation(cancellationToken);
        _ = SynchronizePresentationAsync(document, token, immediate, includeStructure);
    }

    internal void ScheduleOccurrences(SourceDocumentSession document)
    {
        if (document.IsDisposed) return;
        if (!CanUse(document))
        {
            document.Editor.SetOccurrences([]);
            return;
        }
        CancellationToken token = document.BeginOccurrences(cancellationToken);
        _ = SynchronizeOccurrencesAsync(document, token);
    }

    private async Task SynchronizeDiagnosticsAsync(
        SourceDocumentSession document,
        WorkbenchCodeBufferVersion version,
        CancellationToken requestCancellation,
        bool immediate)
    {
        try
        {
            if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(250), requestCancellation);
            WorkbenchCodeSessionId? codeSession = await EnsureSessionAsync(document, requestCancellation);
            if (codeSession is null || !document.IsCurrentDiagnostics(version)) return;
            WorkbenchCodeDiagnosticView result = await service.SynchronizeAsync(new(
                codeSession,
                new(document.View.Path.Value),
                new(document.View.Sha256!.Value),
                version,
                new(document.Editor.Text)), requestCancellation);
            if (!document.IsCurrentDiagnostics(version) ||
                result.State is WorkbenchCodeResultState.Stale or WorkbenchCodeResultState.Cancelled)
                return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (document.Document.Id is not { } id ||
                    !documents().TryGetValue(id, out SourceDocumentSession? current) ||
                    !ReferenceEquals(current, document) || !document.IsCurrentDiagnostics(version))
                    return;
                document.Surface.UpdateCodeHealth(result);
                problems.Set(id, document.View.GoalId, result);
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                problems.Status.Text = $"Code intelligence failed · {exception.Message}");
        }
    }

    private async Task SynchronizePresentationAsync(
        SourceDocumentSession document,
        CancellationToken requestCancellation,
        bool immediate,
        bool includeStructure)
    {
        try
        {
            if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(90), requestCancellation);
            WorkbenchCodeSessionId? codeSession = await EnsureSessionAsync(document, requestCancellation);
            if (codeSession is null || !document.IsCurrentPresentation(requestCancellation)) return;
            WorkbenchCodeBufferVersion version = new(Math.Max(1, document.CurrentBufferVersion));
            WorkbenchCodeDocumentPresentationView? result = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                result = await service.GetDocumentPresentationAsync(new(
                    Snapshot(document, codeSession, version),
                    document.Editor.GetVisibleRange(),
                    includeStructure
                        ? WorkbenchCodeDocumentPresentationScope.ClassificationAndStructure
                        : WorkbenchCodeDocumentPresentationScope.VisibleClassification,
                    new(preferences.ShowParameterNameHints, preferences.ShowInferredTypeHints),
                    new(
                        preferences.ShowReferenceCodeLens,
                        preferences.ShowImplementationCodeLens,
                        preferences.ShowTestCodeLens,
                        ShowRun: preferences.ShowRunCodeLens &&
                            executionService?.Capabilities.CanRunProjectEntryPoint is true,
                        ShowDebug: preferences.ShowDebugCodeLens &&
                            executionService?.Capabilities.CanDebugProjectEntryPoint is true)),
                    requestCancellation);
                if (!document.IsCurrentPresentation(requestCancellation) ||
                    result.State is WorkbenchCodeResultState.Cancelled) return;
                if (result.State is not (WorkbenchCodeResultState.Stale or
                    WorkbenchCodeResultState.Failed)) break;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromMilliseconds(250), requestCancellation);
            }
            if (result is null || !document.IsCurrentPresentation(requestCancellation)) return;
            if (result.State is WorkbenchCodeResultState.Stale or WorkbenchCodeResultState.Failed)
            {
                string detail = result.Issues.FirstOrDefault()?.Message.Value ??
                                "Roslyn did not return a current presentation.";
                await Dispatcher.UIThread.InvokeAsync(() =>
                    document.SetStatus($"Semantic presentation unavailable · {detail}"));
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (document.IsCurrentPresentation(requestCancellation))
                    document.Surface.UpdateDocumentPresentation(result);
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                document.SetStatus($"Semantic presentation failed · {exception.Message}"));
        }
    }

    private async Task SynchronizeOccurrencesAsync(
        SourceDocumentSession document,
        CancellationToken requestCancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(140), requestCancellation);
            WorkbenchCodeSessionId? codeSession = await EnsureSessionAsync(document, requestCancellation);
            if (codeSession is null || !document.IsCurrentOccurrence(requestCancellation)) return;
            WorkbenchCodeBufferVersion version = new(Math.Max(1, document.CurrentBufferVersion));
            WorkbenchCodeOccurrenceView result = await service.FindOccurrencesAsync(
                Snapshot(document, codeSession, version), requestCancellation);
            if (!document.IsCurrentOccurrence(requestCancellation) ||
                result.State is WorkbenchCodeResultState.Stale or WorkbenchCodeResultState.Cancelled or
                    WorkbenchCodeResultState.Failed) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (document.IsCurrentOccurrence(requestCancellation))
                    document.Editor.SetOccurrences(result.Occurrences);
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                document.SetStatus($"Occurrence lookup failed · {exception.Message}"));
        }
    }

    internal async ValueTask<WorkbenchCodeSessionId?> EnsureSessionAsync(
        SourceDocumentSession document,
        CancellationToken requestCancellation)
    {
        WorkspaceView? active = activeWorkspace();
        if (active is null || !active.IsTrusted || active.Id != document.View.WorkspaceId.Value)
            return null;
        return await EnsureSessionAsync(
            active, document.View.GoalId, document.View.Branch, requestCancellation);
    }

    internal async ValueTask<WorkbenchCodeSessionId?> EnsureSessionAsync(
        WorkspaceView active,
        GoalId? goalId,
        WorkspaceBranchName? branch,
        CancellationToken requestCancellation)
    {
        string key = $"{active.Id}:{goalId?.Value ?? "original"}:" +
                     $"{branch?.Value ?? active.Branch}:{active.EntryPoint}";
        await sessionGate.WaitAsync(requestCancellation);
        try
        {
            if (sessionId is not null && string.Equals(sessionKey, key, StringComparison.Ordinal))
                return sessionId;
            if (sessionId is not null)
                await service.StopAsync(sessionId, requestCancellation);
            sessionId = null;
            sessionKey = null;
            string entryPoint = Path.IsPathRooted(active.EntryPoint)
                ? Path.GetRelativePath(active.RootPath, active.EntryPoint)
                : active.EntryPoint;
            if (entryPoint == ".." ||
                entryPoint.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() => problems.Status.Text =
                    "Code intelligence unavailable · invalid workspace entry point.");
                return null;
            }
            WorkbenchCodeSessionView started = await service.StartAsync(
                new(new(active.Id), goalId, new(entryPoint)),
                new UiLoadProgress(problems.Status),
                requestCancellation);
            if (started.SessionId is null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => problems.Status.Text =
                    started.Issues.Count == 0
                        ? "Code intelligence unavailable."
                        : $"Code intelligence unavailable · {started.Issues[0].Message.Value}");
                return null;
            }
            sessionId = started.SessionId;
            sessionKey = key;
            return sessionId;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    internal async ValueTask InvalidateAsync()
    {
        try
        {
            await sessionGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        try
        {
            if (sessionId is not null) await service.StopAsync(sessionId, cancellationToken);
            sessionId = null;
            sessionKey = null;
            problems.Clear();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            sessionGate.Release();
        }
    }

    internal static WorkbenchCodeInteractiveSnapshot Snapshot(
        SourceDocumentSession document,
        WorkbenchCodeSessionId codeSession,
        WorkbenchCodeBufferVersion version,
        WorkbenchCodePosition? requestedPosition = null) => new(
        codeSession,
        new(document.View.Path.Value),
        new(document.View.Sha256!.Value),
        version,
        new(document.Editor.Text),
        requestedPosition ?? document.Editor.CaretPosition);

    internal static bool CanUse(SourceDocumentSession document) =>
        document.View.Sha256 is not null && !document.View.IsTruncated &&
        Path.GetExtension(document.View.Path.Value).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private sealed class UiLoadProgress(global::Avalonia.Controls.TextBlock status)
        : IProgress<WorkbenchCodeLoadProgress>
    {
        public void Report(WorkbenchCodeLoadProgress value) => Dispatcher.UIThread.Post(() =>
            status.Text = $"{value.Stage} · {value.Message.Value}");
    }
}
