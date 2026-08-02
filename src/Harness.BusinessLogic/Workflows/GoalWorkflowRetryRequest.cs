using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalWorkflowRetryRequest(
    GoalId GoalId,
    GoalWorkflowRetryRole Role,
    MaximumAgentOutputTokens MaximumOutputTokens);
