using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Retrieval;

public sealed record GoalContextRequest(
    GoalId GoalId,
    GoalContextQuery Query,
    MaximumContextMatches MaximumMatches);
