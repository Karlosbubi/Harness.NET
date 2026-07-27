namespace Harness.BusinessLogic.Workflows;

public sealed record WorkflowEvidenceView(
    int Sequence,
    WorkflowEvidenceTitle Title,
    WorkflowEvidenceContent Content);
