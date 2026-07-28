using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Workflows;

public sealed record GoalWorkflowSnapshot(
    GoalWorkflowId Id,
    GoalId GoalId,
    GoalWorkflowState State,
    ReviewCycleCount ReviewCycle,
    IReadOnlyList<GoalTaskView> Tasks,
    IReadOnlyList<GoalWorkflowActivityView> Activities,
    IReadOnlyList<WorkflowEvidenceView> Evidence,
    bool CanResume,
    bool RequiresUserDirection);
