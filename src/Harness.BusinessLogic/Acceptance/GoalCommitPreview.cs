using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Acceptance;

public sealed record GoalCommitPreview(
    GoalId GoalId,
    GoalWorkflowId RunId,
    GoalCommitBranch Branch,
    GoalCommitHead Head,
    GoalCommitDiffHash DiffHash,
    GoalCommitDiff Diff,
    GoalCommitChangedFileCount ChangedFileCount);
