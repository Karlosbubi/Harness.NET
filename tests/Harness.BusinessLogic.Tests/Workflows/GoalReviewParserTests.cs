using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed class GoalReviewParserTests
{
    [Theory]
    [InlineData("accept", "Accept")]
    [InlineData("revise", "Revise")]
    public void Parses_closed_review_decisions(string value, string expected)
    {
        GoalReviewResult result = GoalReviewParser.Parse(
            $"{{\"decision\":\"{value}\",\"summary\":\"Evidence.\"}}");

        Assert.Equal(expected, result.Decision?.ToString());
        Assert.Null(result.Error);
    }

    [Fact]
    public void Rejects_unstructured_or_unknown_review_output()
    {
        Assert.Null(GoalReviewParser.Parse("looks good").Decision);
        Assert.Null(GoalReviewParser.Parse(
            "{\"decision\":\"maybe\",\"summary\":\"Unsure.\"}").Decision);
    }
}
