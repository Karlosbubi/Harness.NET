namespace Harness.DataAccess.Workflows;

public sealed record StoredWorkflowCheckpoint(
    string Id,
    WorkflowRunId RunId,
    int Sequence,
    WorkflowCheckpointKind Kind,
    WorkflowActor Actor,
    WorkflowCheckpointSummary Summary,
    WorkflowEvidenceTitle? EvidenceTitle,
    WorkflowEvidenceContent? EvidenceContent,
    DateTimeOffset CreatedAt);
