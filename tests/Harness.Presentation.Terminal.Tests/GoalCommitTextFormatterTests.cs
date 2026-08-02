using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Terminal.Tests;

public sealed class GoalCommitTextFormatterTests
{
    [Fact]
    public void Formats_the_complete_commit_fingerprint_and_diff()
    {
        GoalCommitApprovalView approval = new(
            new("approval"),
            new GoalId("goal"),
            new GoalWorkflowId("run"),
            new("harness/goal"),
            new(new string('a', 40)),
            new(new string('b', 64)),
            new("diff --git a/file b/file\n+content"),
            new(1),
            new("Commit\n\nHarness-Diff-SHA256: " + new string('b', 64)),
            new("User"),
            new("user@example.test"),
            GoalCommitApprovalState.Pending,
            DecisionReason: null,
            CommitSha: null,
            DateTimeOffset.Parse("2026-07-28T22:00:00Z"),
            DecidedAt: null,
            CompletedAt: null);

        string text = GoalCommitTextFormatter.Format(approval);

        Assert.Contains(approval.ExpectedHead.Value, text, StringComparison.Ordinal);
        Assert.Contains(approval.DiffHash.Value, text, StringComparison.Ordinal);
        Assert.Contains(approval.CommitMessage.Value, text, StringComparison.Ordinal);
        Assert.Contains(approval.Diff.Value, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Committed_branch_handoff_is_explicit_and_deliberately_manual()
    {
        GoalCommitApprovalView approval = new(
            new("approval"),
            new GoalId("goal"),
            new GoalWorkflowId("run"),
            new("harness/goal-goal"),
            new(new string('a', 40)),
            new(new string('b', 64)),
            new("diff"),
            new(1),
            new("Commit\n\nHarness-Diff-SHA256: " + new string('b', 64)),
            new("User"),
            new("user@example.test"),
            GoalCommitApprovalState.Committed,
            DecisionReason: null,
            CommitSha: new(new string('c', 40)),
            RequestedAt: DateTimeOffset.Parse("2026-07-31T15:00:00Z"),
            DecidedAt: DateTimeOffset.Parse("2026-07-31T15:01:00Z"),
            CompletedAt: DateTimeOffset.Parse("2026-07-31T15:01:00Z"));

        string text = GoalCommitTextFormatter.FormatHandoff(approval);

        Assert.Contains("LOCAL ONLY", text, StringComparison.Ordinal);
        Assert.Contains(approval.Branch.Value, text, StringComparison.Ordinal);
        Assert.Contains(approval.CommitSha!.Value, text, StringComparison.Ordinal);
        Assert.Contains("Push this branch", text, StringComparison.Ordinal);
        Assert.Contains("pull request", text, StringComparison.Ordinal);
        Assert.Contains("will not push", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not", text, StringComparison.OrdinalIgnoreCase);
    }
}
