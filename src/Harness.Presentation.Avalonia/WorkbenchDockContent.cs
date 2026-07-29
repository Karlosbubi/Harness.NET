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
}
