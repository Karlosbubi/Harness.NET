using System.Collections.Immutable;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Execution;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Debugging;

internal sealed partial class DeveloperDebuggerService
{
    private async ValueTask EnsureReconciledAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref reconciled) != 0) return;
        await reconciliationGate.WaitAsync(cancellationToken);
        try
        {
            if (reconciled == 0)
            {
                await executionStore.InterruptRunningAsync(
                    timeProvider.GetUtcNow(), reconciliationCutoff, cancellationToken);
                Volatile.Write(ref reconciled, 1);
            }
        }
        finally
        {
            reconciliationGate.Release();
        }
    }

    private ValueTask<StoredDeveloperExecution> StartStoredAsync(
        DeveloperDebugSessionId id,
        WorkbenchWorkspaceRequest workspace,
        WorkbenchWorkspaceContext context,
        DeveloperProjectTarget project,
        WorkbenchExecutionTarget? target,
        DeveloperTestTarget? test,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) => executionStore.StartAsync(new(
            new(id.Value),
            new(workspace.WorkspaceId.Value),
            context.GoalId is null ? null : new(context.GoalId.Value),
            new(context.Description),
            StoredDeveloperExecutionOperation.Debug,
            new(project.ProjectPath.Value),
            project.TargetFramework is null ? null : new(project.TargetFramework.Value),
            project.Configuration is null ? null : new(project.Configuration.Value),
            target is null ? null : new(target.DeclarationId.Value),
            startedAt,
            test is null ? null : new(test.Id.Value),
            test is null ? null : new(test.FullyQualifiedName.Value),
            test is null ? null : StoredDeveloperTestScope.Exact,
            ImmutableArray<StoredDeveloperTestName>.Empty), cancellationToken);

    private async ValueTask CompleteDurablyAsync(
        SessionState state,
        string? errorCode,
        string? error)
    {
        if (!state.TryBeginDurableCompletion()) return;
        DeveloperDebugSessionView view = state.Snapshot();
        DateTimeOffset completedAt = view.CompletedAt ?? timeProvider.GetUtcNow();
        try
        {
            await executionStore.CompleteAsync(new(
                new(view.Id.Value),
                MapDurableState(view.State),
                completedAt,
                view.ExitCode,
                Duration(view.StartedAt, completedAt),
                errorCode,
                error), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Could not complete durable debug session {SessionId}.", view.Id.Value);
        }
    }

    private ValueTask CompleteStartFailureAsync(
        StoredDeveloperExecution stored,
        DateTimeOffset startedAt,
        string errorCode,
        string error) => CompleteStartAsync(
            stored, startedAt, StoredDeveloperExecutionState.Failed, errorCode, error);

    private ValueTask CompleteStartInterruptedAsync(
        StoredDeveloperExecution stored,
        DateTimeOffset startedAt) => CompleteStartAsync(
            stored, startedAt, StoredDeveloperExecutionState.Interrupted,
            "debug_start_cancelled", "Debug startup was cancelled.");

    private async ValueTask CompleteStartAsync(
        StoredDeveloperExecution stored,
        DateTimeOffset startedAt,
        StoredDeveloperExecutionState state,
        string errorCode,
        string error)
    {
        DateTimeOffset completedAt = timeProvider.GetUtcNow();
        try
        {
            await executionStore.CompleteAsync(new(
                stored.Id, state, completedAt, null,
                Duration(startedAt, completedAt), errorCode, error),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Could not complete failed debug startup {SessionId}.", stored.Id.Value);
        }
    }

    private static StoredDeveloperExecutionState MapDurableState(
        DeveloperDebugSessionState state) => state switch
        {
            DeveloperDebugSessionState.Succeeded => StoredDeveloperExecutionState.Succeeded,
            DeveloperDebugSessionState.Failed => StoredDeveloperExecutionState.Failed,
            DeveloperDebugSessionState.Terminated => StoredDeveloperExecutionState.Cancelled,
            DeveloperDebugSessionState.Interrupted => StoredDeveloperExecutionState.Interrupted,
            _ => StoredDeveloperExecutionState.Interrupted,
        };

    private static long Duration(DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        Math.Max(0, checked((long)(completedAt - startedAt).TotalMilliseconds));
}
