using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Agents;

public sealed record GoalModelCatalog(
    GoalId GoalId,
    IReadOnlyList<GoalModelCandidate> Models,
    IReadOnlyList<GoalModelProviderIssue> Issues,
    string? ErrorCode,
    string? Error);
