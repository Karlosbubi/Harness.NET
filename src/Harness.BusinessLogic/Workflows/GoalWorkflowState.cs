namespace Harness.BusinessLogic.Workflows;

public enum GoalWorkflowState
{
    Running,
    AwaitingPlanApproval,
    AwaitingAcceptance,
    NeedsDirection,
    PartiallyCompleted,
    Completed,
    Aborted,
}
