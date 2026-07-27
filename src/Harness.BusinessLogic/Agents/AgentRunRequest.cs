namespace Harness.BusinessLogic.Agents;

public sealed record AgentRunRequest(
    AgentRole Role,
    AgentTask Task);
