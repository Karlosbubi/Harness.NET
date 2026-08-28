using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Events;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    private async ValueTask RunWorkflowAsync(
        GoalId goalId,
        Func<CancellationToken, IAsyncEnumerable<GoalWorkflowSnapshot>> operation,
        CancellationToken cancellationToken,
        string operationName)
    {
        if (Current.Goals.IsBusy || Current.Goals.IsWorkflowRunning)
        {
            return;
        }

        workflowExecution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with
            {
                IsBusy = true,
                IsWorkflowRunning = true,
                WorkflowOperationName = operationName,
                WorkflowOperationStartedAt = TimeProvider.System.GetUtcNow(),
                Status = $"{operationName} started.",
            },
        });
        try
        {
            await foreach (GoalWorkflowSnapshot snapshot in operation(workflowExecution.Token)
                               .WithCancellation(workflowExecution.Token))
            {
                RemoteCostReport? cost = await remoteCostService.GetAsync(
                    goalId,
                    workflowExecution.Token);
                Publish(Current with
                {
                    Goals = Current.Goals with
                    {
                        Workflow = snapshot,
                        Cost = cost,
                        Status = WorkflowStatus(snapshot),
                    },
                });
            }

            await ReloadGoalsAsync(
                goalId,
                Current.Goals.Workflow is null
                    ? $"{operationName} returned no workflow snapshot."
                    : WorkflowStatus(Current.Goals.Workflow),
                workflowExecution.Token);
            PublishWorkbenchEvent(
                WorkbenchEventSeverity.Success,
                WorkbenchEventSource.Goal,
                $"{operationName} completed.",
                WorkbenchEventNavigationTarget.Conversation);
        }
        catch (OperationCanceledException) when (workflowExecution.IsCancellationRequested)
        {
            logger.LogInformation("{Operation} cancelled", operationName);
            PublishGoalStatus($"{operationName} cancelled.");
            PublishWorkbenchEvent(
                WorkbenchEventSeverity.Information,
                WorkbenchEventSource.Goal,
                $"{operationName} cancelled.",
                WorkbenchEventNavigationTarget.Conversation);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Operation} failed", operationName);
            PublishGoalStatus(exception.Message);
            PublishWorkbenchEvent(
                WorkbenchEventSeverity.Error,
                WorkbenchEventSource.Goal,
                $"{operationName} failed. Open Conversation for details.",
                WorkbenchEventNavigationTarget.Conversation);
        }
        finally
        {
            workflowExecution.Dispose();
            workflowExecution = null;
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    IsBusy = false,
                    IsWorkflowRunning = false,
                    WorkflowOperationName = null,
                    WorkflowOperationStartedAt = null,
                },
            });
        }
    }
}
