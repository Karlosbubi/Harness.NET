namespace Harness.DataAccess.Workflows;

public sealed record StoredGoalWorkflowCheckpoint(
    string Id,
    GoalWorkflowRunId RunId,
    int Sequence,
    GoalWorkflowCheckpointKind Kind,
    WorkflowActor Actor,
    WorkflowCheckpointSummary Summary,
    WorkflowEvidenceTitle? EvidenceTitle,
    WorkflowEvidenceContent? EvidenceContent,
    DateTimeOffset CreatedAt);
