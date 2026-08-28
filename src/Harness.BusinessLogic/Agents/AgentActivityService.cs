using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

internal sealed class AgentActivityService(TimeProvider timeProvider) : IAgentActivityReader
{
    private const int MaximumActivities = 32;
    private readonly Lock gate = new();
    private readonly Dictionary<AgentActivityId, AgentActivityView> activities = [];

    public event Action? Changed;

    public AgentActivitySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new(activities.Values
                .OrderBy(item => item.StartedAt)
                .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
                .ToArray());
        }
    }

    internal AgentActivityLease BeginProvider(GoalId goalId, AgentRole role) => Begin(
        goalId,
        role,
        AgentActivityKind.ProviderRequest,
        new("model_response"),
        AgentActivityPhase.WaitingForResponse);

    internal AgentActivityLease BeginTool(
        GoalId goalId,
        AgentRole role,
        string operation) => Begin(
            goalId,
            role,
            AgentActivityKind.ToolInvocation,
            new(operation),
            AgentActivityPhase.Running);

    private AgentActivityLease Begin(
        GoalId goalId,
        AgentRole role,
        AgentActivityKind kind,
        AgentActivityOperation operation,
        AgentActivityPhase phase)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        AgentActivityId id = new(Guid.NewGuid().ToString("N"));
        lock (gate)
        {
            activities.Add(id, new(id, goalId, role, kind, operation, phase, now, now));
        }
        NotifyChanged();
        return new(this, id);
    }

    internal void MarkReceiving(AgentActivityId id)
    {
        bool changed = false;
        lock (gate)
        {
            if (activities.TryGetValue(id, out AgentActivityView? current) &&
                current.Phase is AgentActivityPhase.WaitingForResponse)
            {
                activities[id] = current with
                {
                    Phase = AgentActivityPhase.ReceivingResponse,
                    UpdatedAt = timeProvider.GetUtcNow(),
                };
                changed = true;
            }
        }
        if (changed)
        {
            NotifyChanged();
        }
    }

    internal void End(AgentActivityId id, AgentActivityPhase phase)
    {
        bool changed = false;
        lock (gate)
        {
            if (activities.TryGetValue(id, out AgentActivityView? current) && IsActive(current.Phase))
            {
                activities[id] = current with
                {
                    Phase = phase,
                    UpdatedAt = timeProvider.GetUtcNow(),
                };
                TrimCompleted();
                changed = true;
            }
        }
        if (changed)
        {
            NotifyChanged();
        }
    }

    private void TrimCompleted()
    {
        foreach (AgentActivityId id in activities.Values
                     .Where(item => !IsActive(item.Phase))
                     .OrderBy(item => item.UpdatedAt)
                     .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
                     .Take(Math.Max(0, activities.Count - MaximumActivities))
                     .Select(item => item.Id)
                     .ToArray())
        {
            activities.Remove(id);
        }
    }

    private static bool IsActive(AgentActivityPhase phase) => phase is
        AgentActivityPhase.WaitingForResponse or
        AgentActivityPhase.ReceivingResponse or
        AgentActivityPhase.Running;

    private void NotifyChanged()
    {
        Action? changed = Changed;
        if (changed is null)
        {
            return;
        }

        foreach (Action observer in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                observer();
            }
            catch (Exception)
            {
                // Activity visibility is advisory and must never interrupt an agent operation.
            }
        }
    }
}

internal sealed class AgentActivityLease(
    AgentActivityService service,
    AgentActivityId id) : IDisposable
{
    private int disposed;

    internal void MarkReceiving()
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            service.MarkReceiving(id);
        }
    }

    internal void Complete() => End(AgentActivityPhase.Completed);

    internal void Fail() => End(AgentActivityPhase.Failed);

    internal void Cancel() => End(AgentActivityPhase.Cancelled);

    public void Dispose()
    {
        End(AgentActivityPhase.Cancelled);
    }

    private void End(AgentActivityPhase phase)
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            service.End(id, phase);
        }
    }
}
