using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Harness.Presentation.Avalonia;

internal static class WorkbenchDockContent
{
    internal static void Attach(IDockable dockable, object content)
    {
        dockable.Context = content;
        switch (dockable)
        {
            case IDocumentContent document:
                document.Content = content;
                break;
            case IToolContent tool:
                tool.Content = content;
                break;
            default:
                throw new InvalidOperationException(
                    $"Dockable '{dockable.Id}' does not expose a rendered content contract.");
        }
    }

    internal static void ReleaseFromPresenter(Control content)
    {
        if (content.GetVisualParent() is not ContentPresenter presenter ||
            !ReferenceEquals(presenter.Child, content))
        {
            return;
        }

        presenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
        presenter.UpdateChild();
    }
}
