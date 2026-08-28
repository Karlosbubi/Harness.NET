using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public enum AgentActivityKind
{
    ProviderRequest,
    ToolInvocation,
}

public enum AgentActivityPhase
{
    WaitingForResponse,
    ReceivingResponse,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record AgentActivityId(string Value);

public sealed record AgentActivityOperation(string Value);

public sealed record AgentActivityView(
    AgentActivityId Id,
    GoalId GoalId,
    AgentRole Role,
    AgentActivityKind Kind,
    AgentActivityOperation Operation,
    AgentActivityPhase Phase,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);

public sealed record AgentActivitySnapshot(IReadOnlyList<AgentActivityView> Items);

public interface IAgentActivityReader
{
    event Action? Changed;

    AgentActivitySnapshot GetSnapshot();
}
