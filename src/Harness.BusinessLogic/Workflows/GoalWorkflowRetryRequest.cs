using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalRetryGuidance(string Value);

public sealed record GoalWorkflowRetryRequest(
    GoalId GoalId,
    GoalWorkflowRetryRole Role,
    MaximumAgentOutputTokens MaximumOutputTokens,
    GoalRetryGuidance Guidance);
