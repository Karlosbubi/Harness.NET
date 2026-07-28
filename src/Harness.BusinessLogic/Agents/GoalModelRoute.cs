using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Agents;

internal sealed record GoalModelRoute(
    GoalId GoalId,
    AgentRole Role,
    ModelProviderName ProviderName,
    AgentModel Model,
    ModelAccess Access,
    IModelProvider Provider);

internal sealed record GoalModelRouteResult(
    GoalModelRoute? Route,
    AgentErrorCode? ErrorCode,
    AgentError? Error);

internal interface IGoalModelRouteResolver
{
    ValueTask<GoalModelRouteResult> ResolveAsync(
        GoalId goalId,
        AgentRole role,
        CancellationToken cancellationToken = default);
}
