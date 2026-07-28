namespace Harness.DataAccess.Workflows;

public enum GoalWorkflowRunState
{
    Running,
    AwaitingPlanApproval,
    AwaitingAcceptance,
    NeedsDirection,
    Completed,
}
