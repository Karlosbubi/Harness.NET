using Harness.BusinessLogic.Acceptance;

namespace Harness.Presentation.Terminal;

internal static class GoalCommitTextFormatter
{
    internal static string Format(GoalCommitApprovalView approval) => string.Join(
        "\n",
        $"State: {approval.State}",
        $"Branch: {approval.Branch.Value}",
        $"Expected HEAD: {approval.ExpectedHead.Value}",
        $"Complete diff SHA-256: {approval.DiffHash.Value}",
        $"Changed files: {approval.ChangedFileCount.Value}",
        $"Author: {approval.AuthorName.Value} <{approval.AuthorEmail.Value}>",
        $"Commit message:\n{approval.CommitMessage.Value}",
        approval.DecisionReason is null ? string.Empty :
            $"Decision reason: {approval.DecisionReason.Value}",
        approval.CommitSha is null ? string.Empty :
            $"Commit SHA: {approval.CommitSha.Value}",
        string.Empty,
        "EXACT APPROVED DIFF",
        approval.Diff.Value);
}
