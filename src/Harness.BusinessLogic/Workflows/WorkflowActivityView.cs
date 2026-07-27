namespace Harness.BusinessLogic.Workflows;

public sealed record WorkflowActivityView(
    int Sequence,
    WorkflowStage Stage,
    WorkflowActor Actor,
    WorkflowSummary Summary);
