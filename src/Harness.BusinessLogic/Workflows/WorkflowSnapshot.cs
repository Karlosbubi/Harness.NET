namespace Harness.BusinessLogic.Workflows;

public sealed record WorkflowSnapshot(
    WorkflowId Id,
    WorkflowState State,
    IReadOnlyList<WorkflowActivityView> Activities,
    IReadOnlyList<WorkflowEvidenceView> Evidence,
    bool CanResume);
