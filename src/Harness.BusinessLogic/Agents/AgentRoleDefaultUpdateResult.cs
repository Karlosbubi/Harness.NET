namespace Harness.BusinessLogic.Agents;

public sealed record AgentRoleDefaultUpdateResult(
    AgentRoleDefault? Value,
    string? ErrorCode,
    string? Error);
