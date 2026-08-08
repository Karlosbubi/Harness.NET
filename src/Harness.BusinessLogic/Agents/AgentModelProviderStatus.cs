namespace Harness.BusinessLogic.Agents;

public sealed record AgentModelProviderStatus(
    ModelProviderName Provider,
    ModelAccess Access,
    AgentModel ConfiguredDefaultModel,
    int DiscoveredChatModels,
    int RoleCompatibleModels,
    bool HasPublishedPricing,
    AgentModelProviderAvailability Availability,
    string? Message);
