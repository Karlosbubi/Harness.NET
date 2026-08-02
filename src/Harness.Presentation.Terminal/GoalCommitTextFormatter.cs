using Harness.BusinessLogic.Acceptance;

namespace Harness.Presentation.Terminal;

internal static class GoalCommitTextFormatter
{
    internal static string Format(GoalCommitApprovalView approval) => string.Join(
        "\n",
        approval.State is GoalCommitApprovalState.Committed
            ? FormatHandoff(approval)
            : string.Empty,
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

    internal static string FormatHandoff(GoalCommitApprovalView approval)
    {
        if (approval.State is not GoalCommitApprovalState.Committed ||
            approval.CommitSha is null)
        {
            return "The goal branch is not committed and ready for handoff.";
        }

        return string.Join(
            "\n",
            "GOAL BRANCH READY (LOCAL ONLY)",
            $"Branch: {approval.Branch.Value}",
            $"Commit: {approval.CommitSha.Value}",
            "Original branch: unchanged",
            string.Empty,
            "Next step is deliberately manual:",
            "1. Push this branch to the remote you choose.",
            "2. Open a pull request, or inspect and merge it with your normal Git workflow.",
            "Harness.NET will not push, open a PR, merge, or rebase automatically.",
            string.Empty);
    }
}
