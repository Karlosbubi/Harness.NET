namespace Harness.DataAccess.Commits;

public sealed record StoredGoalCommitApprovalStart(
    StoredGoalCommitApproval Approval,
    bool WasCreated);
