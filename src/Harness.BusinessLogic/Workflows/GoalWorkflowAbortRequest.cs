using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalAbortReason(string Value);

public sealed record GoalWorkflowAbortRequest(
    GoalId GoalId,
    GoalAbortReason Reason);
