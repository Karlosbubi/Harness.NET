using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Terminal;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Terminal;

internal sealed partial class DeveloperTerminalService
{
    private readonly SemaphoreSlim reconciliationGate = new(1, 1);
    private readonly DateTimeOffset reconciliationCutoff = timeProvider.GetUtcNow();
    private int reconciled;

    private async ValueTask EnsureReconciledAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref reconciled) != 0) return;
        await reconciliationGate.WaitAsync(cancellationToken);
        try
        {
            if (reconciled != 0) return;
            await sessionStore.InterruptRunningAsync(
                timeProvider.GetUtcNow(),
                reconciliationCutoff,
                cancellationToken);
            Volatile.Write(ref reconciled, 1);
        }
        finally
        {
            reconciliationGate.Release();
        }
    }

    private async ValueTask PersistStartAsync(
        DeveloperTerminalSessionView view,
        CancellationToken cancellationToken)
    {
        await sessionStore.StartAsync(new(
                new(view.Id.Value),
                new(view.WorkspaceId.Value),
                view.SourceContext.GoalId is null ? null : new(view.SourceContext.GoalId.Value),
                view.SourceContext.Scope switch
                {
                    WorkbenchWorkspaceScope.OriginalWorkspace => StoredTerminalSourceScope.OriginalWorkspace,
                    WorkbenchWorkspaceScope.ApprovedGoalWorktree => StoredTerminalSourceScope.ApprovedGoalWorktree,
                    _ => throw new InvalidOperationException("A terminal requires an available source scope."),
                },
                view.SourceContext.Branch is null ? null : new(view.SourceContext.Branch.Value),
                new(view.SourceContext.Description),
                new(view.WorkingDirectory.Value),
                new(view.Shell.Value),
                StoredTerminalEnvironmentProfile.InheritedLocked,
                StoredTerminalContentPolicy.Transient,
                new(view.Dimensions.Columns, view.Dimensions.Rows),
                view.StartedAt),
            cancellationToken);
    }

    private async ValueTask PersistCompletionSafelyAsync(DeveloperTerminalSessionView view)
    {
        if (view.State == DeveloperTerminalSessionState.Running || view.CompletedAt is null)
        {
            return;
        }

        try
        {
            await sessionStore.CompleteAsync(new(
                new(view.Id.Value),
                view.State switch
                {
                    DeveloperTerminalSessionState.Exited => StoredTerminalSessionState.Exited,
                    DeveloperTerminalSessionState.Stopped => StoredTerminalSessionState.Stopped,
                    DeveloperTerminalSessionState.Failed => StoredTerminalSessionState.Failed,
                    DeveloperTerminalSessionState.Interrupted => StoredTerminalSessionState.Interrupted,
                    _ => throw new InvalidOperationException("A running terminal cannot be completed."),
                },
                view.CompletedAt.Value,
                view.ExitCode,
                view.ErrorCode,
                view.Error), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Developer terminal completion metadata update failed");
        }
    }

    private static DeveloperTerminalSessionView Map(StoredTerminalSession stored) => new(
        new(stored.Id.Value),
        new(stored.WorkspaceId.Value),
        new(
            new(stored.WorkspaceId.Value),
            stored.GoalId is null ? null : new(stored.GoalId.Value),
            stored.SourceBranch is null ? null : new(stored.SourceBranch.Value),
            stored.SourceScope switch
            {
                StoredTerminalSourceScope.OriginalWorkspace => WorkbenchWorkspaceScope.OriginalWorkspace,
                StoredTerminalSourceScope.ApprovedGoalWorktree => WorkbenchWorkspaceScope.ApprovedGoalWorktree,
                _ => WorkbenchWorkspaceScope.Unavailable,
            },
            stored.SourceDescription.Value),
        new(stored.WorkingDirectory.Value),
        new(stored.Shell.Value),
        new("Inherited host environment with locked terminal policy"),
        new("Transient · content expired after restart"),
        new(stored.Dimensions.Columns, stored.Dimensions.Rows),
        stored.State switch
        {
            StoredTerminalSessionState.Running => DeveloperTerminalSessionState.Running,
            StoredTerminalSessionState.Exited => DeveloperTerminalSessionState.Exited,
            StoredTerminalSessionState.Stopped => DeveloperTerminalSessionState.Stopped,
            StoredTerminalSessionState.Failed => DeveloperTerminalSessionState.Failed,
            StoredTerminalSessionState.Interrupted => DeveloperTerminalSessionState.Interrupted,
            _ => DeveloperTerminalSessionState.Failed,
        },
        stored.StartedAt,
        stored.CompletedAt,
        stored.ExitCode,
        IsTrusted: true,
        stored.ErrorCode,
        stored.Error);
}
