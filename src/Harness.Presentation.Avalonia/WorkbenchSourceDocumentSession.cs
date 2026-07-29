using Avalonia.Automation;
using Avalonia.Controls;
using AvaloniaEdit;
using Dock.Model.Avalonia.Controls;
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

    internal SourceDocumentSession(
        SourceDockDocument document,
        TextEditor editor,
        TextBlock status,
        Button save,
        Button reload,
        Button close,
        WorkbenchDocumentView view)
    {
        Document = document;
        Editor = editor;
        Status = status;
        Save = save;
        Reload = reload;
        Close = close;
        View = view;
    }

    internal SourceDockDocument Document { get; }
    internal TextEditor Editor { get; }
    internal TextBlock Status { get; }
    internal Button Save { get; }
    internal Button Reload { get; }
    internal Button Close { get; }
    internal WorkbenchDocumentView View { get; private set; }
    internal bool IsDirty { get; private set; }
    internal bool AllowClose { get; set; }
    internal bool IgnoreNextActivationChange { get; set; }

    internal void SynchronizeDirtyState()
    {
        if (suppressChanges)
        {
            return;
        }

        IsDirty = View.Access is WorkbenchDocumentAccess.Editable &&
                  !string.Equals(Editor.Text, View.Content.Value, StringComparison.Ordinal);
        Document.IsModified = IsDirty;
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
        Document.CloseRequested = null;
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
