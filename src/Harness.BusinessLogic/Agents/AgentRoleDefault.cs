namespace Harness.BusinessLogic.Agents;

public sealed record AgentRoleDefault(
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model,
    ModelAccess Access,
    AgentReasoningPolicy ReasoningPolicy,
    bool IsPersisted,
    DateTimeOffset? UpdatedAt);
