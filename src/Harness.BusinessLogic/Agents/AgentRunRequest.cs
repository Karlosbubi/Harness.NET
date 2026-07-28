using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public sealed record AgentRunRequest(
    GoalId GoalId,
    AgentRole Role,
    AgentTask Task,
    MaximumAgentOutputTokens? MaximumOutputTokens = null);
