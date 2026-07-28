using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalWorkflowSnapshot(
    GoalWorkflowId Id,
    GoalId GoalId,
    GoalWorkflowState State,
    IReadOnlyList<GoalWorkflowActivityView> Activities,
    IReadOnlyList<WorkflowEvidenceView> Evidence,
    bool CanResume,
    bool RequiresUserDirection);
