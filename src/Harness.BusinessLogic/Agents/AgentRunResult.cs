namespace Harness.BusinessLogic.Agents;

public sealed record AgentRunResult(
    AgentRole Role,
    AgentOutput? Output,
    AgentErrorCode? ErrorCode,
    AgentError? Error);
