using Avalonia.Automation;
using Avalonia.Controls;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow
{
    private readonly AgentActivityStatusControl agentActivityStatus;

    private Control BuildFooter()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new("*,Auto"),
            Margin = new(10, 3),
        };
        AutomationProperties.SetName(status, "Application status");
        grid.Children.Add(status);
        Grid.SetColumn(budget, 1);
        grid.Children.Add(budget);
        return grid;
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        store.WorkbenchEventPublished -= OnWorkbenchEventPublished;
        workbenchEvents.Dispose();
        agentActivityStatus.Dispose();
        subscriptions.Dispose();
    }
}
