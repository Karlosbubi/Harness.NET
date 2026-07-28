using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Terminal.Tests;

public sealed class GoalWorkflowTextFormatterTests
{
    [Fact]
    public void Formats_persisted_activity_evidence_and_direction_state()
    {
        GoalWorkflowSnapshot snapshot = new(
            new("run-1"),
            new GoalId("goal-1"),
            GoalWorkflowState.NeedsDirection,
            [new(1, GoalWorkflowCheckpointKind.UserDirectionRequired,
                WorkflowActor.System, new("Uncertain call was not replayed."))],
            [new(1, new("Recovery notice"), new("Inspect cost evidence."))],
            CanResume: false,
            RequiresUserDirection: true);

        string value = GoalWorkflowTextFormatter.Format(snapshot);

        Assert.Contains("NeedsDirection", value, StringComparison.Ordinal);
        Assert.Contains("user direction required", value, StringComparison.Ordinal);
        Assert.Contains("UserDirectionRequired", value, StringComparison.Ordinal);
        Assert.Contains("Inspect cost evidence.", value, StringComparison.Ordinal);
    }
}
