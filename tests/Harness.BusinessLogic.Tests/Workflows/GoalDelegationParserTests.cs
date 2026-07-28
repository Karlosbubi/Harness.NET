using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed class GoalDelegationParserTests
{
    [Fact]
    public void Parses_exact_bounded_delegation()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Slice","objective":"Implement slice","fileAreas":["src/A"],"acceptanceCriteria":["Tests pass","Build passes"]}]}
            """);

        Assert.Null(result.Error);
        GoalDelegatedTask task = Assert.Single(result.Tasks);
        Assert.Equal("Slice", task.Title.Value);
        Assert.Equal("- src/A", task.FileAreas.Value);
        Assert.Contains("- Tests pass", task.AcceptanceCriteria.Value,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"plan\":\"Plan\",\"tasks\":[]}")]
    [InlineData("{\"plan\":\"Plan\",\"tasks\":[{\"title\":\"Task\"}]}")]
    [InlineData("{\"plan\":\"Plan\",\"tasks\":[],\"extra\":true}")]
    [InlineData("{\"plan\":\"Plan\",\"tasks\":[{\"title\":\"Task\",\"objective\":\"Do it\",\"fileAreas\":[\"../outside\"],\"acceptanceCriteria\":[\"Pass\"]}]}")]
    public void Rejects_unbounded_or_incomplete_delegation(string value)
    {
        GoalDelegation result = GoalDelegationParser.Parse(value);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Tasks);
    }
}
