namespace Harness.DataAccess.Agents;

public sealed record StoredAgentRoleDefault(
    AgentDefaultRole Role,
    AgentDefaultProvider Provider,
    AgentDefaultModel Model,
    DateTimeOffset UpdatedAt);
