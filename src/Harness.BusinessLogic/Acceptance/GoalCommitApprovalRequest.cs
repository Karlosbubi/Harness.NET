using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Acceptance;

public sealed record GoalCommitApprovalRequest(
    GoalId GoalId,
    GoalWorkflowId RunId,
    GoalCommitHead ExpectedHead,
    GoalCommitDiffHash ExpectedDiffHash,
    GoalCommitMessage Message,
    GoalCommitAuthorName AuthorName,
    GoalCommitAuthorEmail AuthorEmail);
