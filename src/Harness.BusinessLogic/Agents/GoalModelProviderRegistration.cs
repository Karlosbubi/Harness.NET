using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Agents;

internal sealed record GoalModelProviderRegistration(
    ModelProviderName Name,
    ModelAccess Access,
    AgentModel DefaultModel,
    IModelProvider Provider);
