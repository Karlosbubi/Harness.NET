namespace Harness.BusinessLogic.Workflows;

public enum GoalWorkflowState
{
    Running,
    AwaitingPlanApproval,
    AwaitingAcceptance,
    NeedsDirection,
    Completed,
    Aborted,
}
