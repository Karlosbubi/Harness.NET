using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public sealed record GoalModelSelectionRequest(
    GoalId GoalId,
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model);
