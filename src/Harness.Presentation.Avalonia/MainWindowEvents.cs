using Avalonia.Threading;
using Harness.BusinessLogic.Events;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow
{
    private async Task ShowOperationsAsync() =>
        await new OperationsDialog(store, cancellationToken).ShowDialog(this);

    private void OnWorkbenchEventPublished(WorkbenchEvent workbenchEvent) =>
        Dispatcher.UIThread.Post(() => workbenchEvents.Publish(workbenchEvent));

    private void NavigateToWorkbenchEvent(WorkbenchEventNavigationTarget target)
    {
        switch (target)
        {
            case WorkbenchEventNavigationTarget.Conversation:
                ShowConversation();
                break;
            case WorkbenchEventNavigationTarget.Git:
                workbench?.ShowGit();
                break;
            case WorkbenchEventNavigationTarget.RunOutput:
                workbench?.ShowRunOutput();
                break;
            case WorkbenchEventNavigationTarget.Problems:
                workbench?.ShowProblems();
                break;
            case WorkbenchEventNavigationTarget.Operations:
                _ = ShowOperationsAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
