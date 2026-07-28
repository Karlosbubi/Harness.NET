using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalWorkflowResumeRequest(
    GoalId GoalId,
    MaximumAgentOutputTokens ImplementerMaximumOutputTokens,
    MaximumAgentOutputTokens ReviewerMaximumOutputTokens);
