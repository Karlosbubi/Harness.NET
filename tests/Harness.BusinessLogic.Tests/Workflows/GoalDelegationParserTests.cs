using Harness.BusinessLogic.Workflows;

namespace Harness.BusinessLogic.Tests.Workflows;

public sealed class GoalDelegationParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reports_empty_lead_output_with_recovery_guidance(string value)
    {
        GoalDelegation result = GoalDelegationParser.Parse(value);

        Assert.Equal(
            "The Lead returned no plan. Retry with another Lead model or add guidance.",
            result.Error);
        Assert.Empty(result.Tasks);
    }

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
    [InlineData("```json\n{\"plan\":\"Plan\",\"tasks\":[{\"title\":\"Slice\",\"objective\":\"Implement slice\",\"fileAreas\":[\"src/A\"],\"acceptanceCriteria\":[\"Build passes\"]}]}\n```")]
    [InlineData("```\n{\"plan\":\"Plan\",\"tasks\":[{\"title\":\"Slice\",\"objective\":\"Implement slice\",\"fileAreas\":[\"src/A\"],\"acceptanceCriteria\":[\"Build passes\"]}]}\n```")]
    public void Parses_one_exact_markdown_json_fence(string value)
    {
        GoalDelegation result = GoalDelegationParser.Parse(value);

        Assert.Null(result.Error);
        Assert.Single(result.Tasks);
    }

    [Fact]
    public void Rejects_prose_around_a_json_fence()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            Here is the plan:
            ```json
            {"plan":"Plan","tasks":[]}
            ```
            """);

        Assert.NotNull(result.Error);
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

    [Fact]
    public void Identifies_the_invalid_task_and_field()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"First","objective":"Implement first","fileAreas":["src/A"],"acceptanceCriteria":["Pass"]},{"title":"Second","objective":"Implement second","fileAreas":[],"acceptanceCriteria":["Pass"]}]}
            """);

        Assert.Equal(
            "Lead task 2 fileAreas must contain 1-32 valid repository-relative paths.",
            result.Error);
    }

    [Fact]
    public void Rejects_standalone_inspection_task()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Inspect workspace","objective":"Assess the existing solution layout.","fileAreas":["src/A"],"acceptanceCriteria":["Layout is documented."]}]}
            """);

        Assert.Contains("Standalone discovery", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public void Ignores_standalone_inspection_when_durable_work_remains()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Inspect workspace","objective":"Assess the existing solution layout.","fileAreas":["src/A"],"acceptanceCriteria":["Layout is documented."]},{"title":"Implement engine","objective":"Implement the immutable game engine.","fileAreas":["src/A"],"acceptanceCriteria":["Build passes."]}]}
            """);

        Assert.Null(result.Error);
        GoalDelegatedTask task = Assert.Single(result.Tasks);
        Assert.Equal("Implement engine", task.Title.Value);
        Assert.Contains("ignored 1 standalone", result.Plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Allows_inspection_folded_into_implementation()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Inspect and implement GameState","objective":"Inspect the contract, then implement the immutable engine.","fileAreas":["src/A"],"acceptanceCriteria":["Build passes."]}]}
            """);

        Assert.Null(result.Error);
        Assert.Single(result.Tasks);
    }

    [Fact]
    public void Ignores_planning_and_validation_only_tasks_while_preserving_code_and_test_writes()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Inspect and plan","objective":"Inspect the workspace and create an implementation plan.","fileAreas":["src/A"],"acceptanceCriteria":["Plan exists."]},{"title":"Create engine","objective":"Create the GameState class.","fileAreas":["src/A"],"acceptanceCriteria":["Engine works."]},{"title":"Write tests","objective":"Write deterministic tests.","fileAreas":["tests/A"],"acceptanceCriteria":["Tests cover behavior."]},{"title":"Build and test","objective":"Build and test the solution.","fileAreas":["src/A"],"acceptanceCriteria":["Build passes."]}]}
            """);

        Assert.Null(result.Error);
        Assert.Equal(["Create engine", "Write tests"],
            result.Tasks.Select(task => task.Title.Value));
        Assert.Contains("ignored 2 standalone", result.Plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_validation_task_even_when_its_objective_mentions_the_change_as_a_noun()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Implement accessible names","objective":"Modify the settings fields.","fileAreas":["src/A"],"acceptanceCriteria":["Fields have names."]},{"title":"Verify implementation with narrow tests and build","objective":"Execute a clean build and run the narrow test suite. The change must remain limited to the approved files.","fileAreas":["src/A","tests/A"],"acceptanceCriteria":["Build and tests pass."]}]}
            """);

        Assert.Null(result.Error);
        Assert.Equal("Implement accessible names", Assert.Single(result.Tasks).Title.Value);
        Assert.Contains("ignored 1 standalone", result.Plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_standalone_documentation_task_when_implementation_remains()
    {
        GoalDelegation result = GoalDelegationParser.Parse("""
            {"plan":"Plan","tasks":[{"title":"Implement regression runner","objective":"Implement deterministic scenario execution.","fileAreas":["eng"],"acceptanceCriteria":["Runner works."]},{"title":"Update documentation and task ledger","objective":"Update README and roadmap to mark delivery.","fileAreas":["README.md","docs"],"acceptanceCriteria":["Docs are current."]}]}
            """);

        Assert.Null(result.Error);
        Assert.Equal("Implement regression runner", Assert.Single(result.Tasks).Title.Value);
        Assert.Contains("ignored 1 standalone", result.Plan, StringComparison.Ordinal);
    }
}
