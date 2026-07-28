namespace Harness.BusinessLogic.Workflows;

public enum GoalWorkflowCheckpointKind
{
    Started,
    LeadCallStarted,
    PlanProposed,
    PlanApproved,
    ImplementerCallStarted,
    ImplementationProduced,
    ReviewerCallStarted,
    ReviewCompleted,
    UserDirectionRequired,
    Accepted,
}
