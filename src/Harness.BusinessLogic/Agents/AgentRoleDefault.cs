namespace Harness.BusinessLogic.Agents;

public sealed record AgentRoleDefault(
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model,
    ModelAccess Access,
    bool IsPersisted,
    DateTimeOffset? UpdatedAt);
