namespace Harness.BusinessLogic.Agents;

public sealed record GoalModelCandidate(
    ModelProviderName Provider,
    AgentModel Model,
    ModelAccess Access,
    IReadOnlyList<ModelCapability> Capabilities,
    ModelContextLength? ContextLength,
    UsdPerMillionTokens? InputPrice,
    UsdPerMillionTokens? OutputPrice,
    UsdPerRequest? RequestPrice);
