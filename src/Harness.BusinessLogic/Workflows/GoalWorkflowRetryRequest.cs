using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalRetryGuidance(string Value);

public sealed record GoalWorkflowRetryRequest(
    GoalId GoalId,
    GoalWorkflowRetryRole Role,
    GoalRetryGuidance? Guidance = null);
