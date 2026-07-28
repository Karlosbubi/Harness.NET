using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalWorkflowStartRequest(
    GoalId GoalId,
    MaximumAgentOutputTokens LeadMaximumOutputTokens);
