using Avalonia.Automation;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Dock.Model.Avalonia.Controls;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;

namespace Harness.Presentation.Avalonia;

internal sealed class SourceDockDocument : Document
{
    internal Func<bool>? CloseRequested { get; set; }

    public override bool OnClose() => CloseRequested?.Invoke() ?? base.OnClose();
}

internal sealed class SourceDocumentSession : IDisposable
{
    private bool suppressChanges;
    private bool isBusy;
    private CancellationTokenSource? diagnosticsCancellation;
    private CancellationTokenSource? interactionCancellation;
    private CancellationTokenSource? hoverCancellation;
    private long bufferVersion;

    internal SourceDocumentSession(
        SourceDockDocument document,
        SourceEditorSurface surface,
        WorkbenchDocumentView view)
    {
        Document = document;
        Surface = surface;
        View = view;
    }

    internal SourceDockDocument Document { get; }
    internal SourceEditorSurface Surface { get; }
    internal TextEditor Editor => Surface.Editor;
    internal TextBlock Status => Surface.Status;
    internal Button Save => Surface.Save;
    internal Button Reload => Surface.Reload;
    internal Button Close => Surface.Close;
    internal WorkbenchDocumentView View { get; private set; }
    internal bool IsDirty { get; private set; }
    internal bool AllowClose { get; set; }
    internal bool IgnoreNextActivationChange { get; set; }
    internal long CurrentBufferVersion => bufferVersion;
    internal CompletionWindow? CompletionWindow { get; set; }
    internal InsightWindow? QuickInfoWindow { get; set; }
    internal OverloadInsightWindow? SignatureWindow { get; set; }

    internal (WorkbenchCodeBufferVersion Version, CancellationToken Token) BeginDiagnostics(
        CancellationToken parentCancellation)
    {
        diagnosticsCancellation?.Cancel();
        diagnosticsCancellation?.Dispose();
        diagnosticsCancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        return (new(++bufferVersion), diagnosticsCancellation.Token);
    }

    internal bool IsCurrentDiagnostics(WorkbenchCodeBufferVersion version) =>
        bufferVersion == version.Value && diagnosticsCancellation?.IsCancellationRequested is false;

    internal (WorkbenchCodeBufferVersion Version, CancellationToken Token) BeginInteraction(
        CancellationToken parentCancellation)
    {
        interactionCancellation?.Cancel();
        interactionCancellation?.Dispose();
        interactionCancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        return (new(Math.Max(1, bufferVersion)), interactionCancellation.Token);
    }

    internal bool IsCurrentInteraction(WorkbenchCodeBufferVersion version) =>
        bufferVersion == version.Value && interactionCancellation?.IsCancellationRequested is false;

    internal CancellationToken BeginHover(CancellationToken parentCancellation)
    {
        hoverCancellation?.Cancel();
        hoverCancellation?.Dispose();
        hoverCancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        return hoverCancellation.Token;
    }

    internal void CancelHover() => hoverCancellation?.Cancel();

    internal void CloseInteractiveWindows()
    {
        CompletionWindow?.Hide();
        QuickInfoWindow?.Hide();
        SignatureWindow?.Hide();
        CompletionWindow = null;
        QuickInfoWindow = null;
        SignatureWindow = null;
    }

    internal void SynchronizeDirtyState()
    {
        if (suppressChanges)
        {
            return;
        }

        IsDirty = View.Access is WorkbenchDocumentAccess.Editable &&
                  !string.Equals(Editor.Text, View.Content.Value, StringComparison.Ordinal);
        Document.IsModified = IsDirty;
        Surface.UpdateMetrics();
        Save.IsEnabled = !isBusy && IsDirty &&
                         View.Access is WorkbenchDocumentAccess.Editable;
        if (IsDirty)
        {
            Status.Text = "Unsaved changes · save uses the loaded worktree version as its baseline.";
        }
        else if (!isBusy)
        {
            Status.Text = View.AccessDescription;
        }
    }

    internal void AcceptSaved(
        WorkbenchDocumentSha256 hash,
        WorkbenchDocumentByteCount bytesWritten)
    {
        View = View with
        {
            Content = new(Editor.Text),
            Sha256 = hash,
            Size = bytesWritten,
        };
        IsDirty = false;
        Document.IsModified = false;
        Save.IsEnabled = false;
        Status.Text = $"Saved {bytesWritten.Value:N0} bytes to {View.Branch?.Value ?? "the goal worktree"}.";
    }

    internal void ReplaceWith(WorkbenchDocumentView view)
    {
        suppressChanges = true;
        try
        {
            View = view;
            Editor.Text = view.Content.Value;
            Editor.IsReadOnly = view.Access is not WorkbenchDocumentAccess.Editable;
            Surface.UpdateView(view);
            AutomationProperties.SetName(
                Editor,
                view.Access is WorkbenchDocumentAccess.Editable
                    ? $"Editable source editor for {view.Path.Value}"
                    : $"Read-only source editor for {view.Path.Value}");
            Document.Title = Title(view);
            IsDirty = false;
            Document.IsModified = false;
            Status.Text = $"Reloaded · {view.AccessDescription}";
        }
        finally
        {
            suppressChanges = false;
            SynchronizeDirtyState();
        }
    }

    internal void DiscardChanges()
    {
        suppressChanges = true;
        try
        {
            Editor.Text = View.Content.Value;
            IsDirty = false;
            Document.IsModified = false;
            Status.Text = "Unsaved changes discarded.";
        }
        finally
        {
            suppressChanges = false;
            SynchronizeDirtyState();
        }
    }

    internal void SetBusy(bool busy, string? message = null)
    {
        isBusy = busy;
        Editor.IsEnabled = !busy;
        Save.IsEnabled = !busy && IsDirty &&
                         View.Access is WorkbenchDocumentAccess.Editable;
        Reload.IsEnabled = !busy;
        Close.IsEnabled = !busy;
        if (message is not null)
        {
            Status.Text = message;
        }
    }

    internal void SetStatus(string message) => Status.Text = message;

    public void Dispose()
    {
        diagnosticsCancellation?.Cancel();
        diagnosticsCancellation?.Dispose();
        interactionCancellation?.Cancel();
        interactionCancellation?.Dispose();
        hoverCancellation?.Cancel();
        hoverCancellation?.Dispose();
        CloseInteractiveWindows();
        Document.CloseRequested = null;
        Surface.Dispose();
    }

    private static string Title(WorkbenchDocumentView view)
    {
        string title = Path.GetFileName(view.Path.Value);
        if (view.IsTruncated)
        {
            return $"{title} · truncated";
        }

        return view.Branch is null ? title : $"{title} · {view.Branch.Value}";
    }
}
