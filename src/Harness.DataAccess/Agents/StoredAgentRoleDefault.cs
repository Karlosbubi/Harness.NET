namespace Harness.DataAccess.Agents;

public sealed record StoredAgentRoleDefault(
    AgentDefaultRole Role,
    AgentDefaultProvider Provider,
    AgentDefaultModel Model,
    AgentDefaultMaximumOutputTokens MaximumOutputTokens,
    DateTimeOffset UpdatedAt);
