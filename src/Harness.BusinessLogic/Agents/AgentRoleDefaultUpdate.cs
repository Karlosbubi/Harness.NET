namespace Harness.BusinessLogic.Agents;

public sealed record AgentRoleDefaultUpdate(
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model,
    MaximumAgentOutputTokens MaximumOutputTokens);
