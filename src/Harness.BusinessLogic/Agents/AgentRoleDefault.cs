namespace Harness.BusinessLogic.Agents;

public sealed record AgentRoleDefault(
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model,
    ModelAccess Access,
    MaximumAgentOutputTokens MaximumOutputTokens,
    bool IsPersisted,
    DateTimeOffset? UpdatedAt);
