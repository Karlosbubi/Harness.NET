using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Terminal.Tests;

public sealed class WorkflowTextFormatterTests
{
    [Fact]
    public void Formats_expandable_activity_and_full_evidence_content()
    {
        WorkflowSnapshot snapshot = new(
            new("run-1"),
            WorkflowState.Paused,
            [
                new(1, WorkflowStage.Started, WorkflowActor.System, new("Run started")),
                new(2, WorkflowStage.Planning, WorkflowActor.Lead, new("Plan proposed")),
            ],
            [
                new(2, new("Proposed plan"), new("Implement, verify, review")),
            ],
            CanResume: true);

        string activity = WorkflowTextFormatter.FormatActivity(snapshot);
        string evidence = WorkflowTextFormatter.FormatEvidence(snapshot);

        Assert.Contains("2. Lead [Planning]", activity, StringComparison.Ordinal);
        Assert.Contains("Plan proposed", activity, StringComparison.Ordinal);
        Assert.Contains("CHECKPOINT 2: Proposed plan", evidence, StringComparison.Ordinal);
        Assert.Contains("Implement, verify, review", evidence, StringComparison.Ordinal);
    }
}
