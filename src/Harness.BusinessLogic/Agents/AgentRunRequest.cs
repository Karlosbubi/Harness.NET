using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public sealed record AgentRunRequest(
    GoalId GoalId,
    AgentRole Role,
    AgentTask Task,
    IReadOnlyList<AgentFileArea>? FileAreas = null);
