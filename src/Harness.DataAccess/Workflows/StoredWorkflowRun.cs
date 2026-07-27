namespace Harness.DataAccess.Workflows;

public sealed record StoredWorkflowRun(
    WorkflowRunId Id,
    WorkflowRunState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
