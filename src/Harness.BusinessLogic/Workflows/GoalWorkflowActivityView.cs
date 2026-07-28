namespace Harness.BusinessLogic.Workflows;

public sealed record GoalWorkflowActivityView(
    int Sequence,
    GoalWorkflowCheckpointKind Kind,
    WorkflowActor Actor,
    WorkflowSummary Summary);
