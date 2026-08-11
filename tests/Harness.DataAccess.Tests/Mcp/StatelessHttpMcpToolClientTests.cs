using Harness.DataAccess.Mcp;

namespace Harness.DataAccess.Tests.Mcp;

public sealed class StatelessHttpMcpToolClientTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, null, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(null, false, false)]
    [InlineData(null, null, false)]
    public void Agent_eligibility_fails_closed_on_missing_or_mutating_metadata(
        bool? readOnlyHint,
        bool? destructiveHint,
        bool expected) =>
        Assert.Equal(expected, StatelessHttpMcpToolClient.IsAgentEligible(
            readOnlyHint, destructiveHint));

    [Fact]
    public void Catalog_policy_rejects_duplicate_names()
    {
        int eligibleCount = 0;

        McpToolDefinition result = StatelessHttpMcpToolClient.ApplyCatalogPolicy(
            Tool("lookup"), new HashSet<string>(["lookup"], StringComparer.Ordinal),
            ref eligibleCount);

        Assert.False(result.IsAgentEligible);
        Assert.Contains("more than once", result.RejectionReason);
        Assert.Equal(0, eligibleCount);
    }

    [Fact]
    public void Catalog_policy_bounds_agent_tools_per_connection()
    {
        int eligibleCount = StatelessHttpMcpToolClient.MaximumEligibleToolsPerConnection;

        McpToolDefinition result = StatelessHttpMcpToolClient.ApplyCatalogPolicy(
            Tool("excess"), new HashSet<string>(StringComparer.Ordinal), ref eligibleCount);

        Assert.False(result.IsAgentEligible);
        Assert.Contains("first 32", result.RejectionReason);
    }

    [Theory]
    [InlineData("harness_application", true)]
    [InlineData("harness_create_goal", true)]
    [InlineData("harness_abort_goal", false)]
    [InlineData("shell", false)]
    public void Harness_control_requires_exact_prefixed_allowlist(
        string toolName,
        bool expected)
    {
        McpConnectionConfiguration configuration = new(
            new("worker"),
            new(new Uri("http://127.0.0.1:57431/mcp")),
            new(TimeSpan.FromSeconds(30)),
            IsEnabled: true,
            RequiresRestart: false,
            McpConnectionAccess.HarnessControl,
            new("controller"),
            new("worker-token"),
            [new("harness_application"), new("harness_create_goal")]);

        Assert.Equal(expected,
            StatelessHttpMcpToolClient.IsHarnessControlEligible(configuration, toolName));
    }

    private static McpToolDefinition Tool(string name)
    {
        using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
            "{\"type\":\"object\"}");
        return new(
            new("docs"),
            new(name),
            null,
            name,
            schema.RootElement.Clone(),
            null,
            IsReadOnly: true,
            IsDestructive: false,
            IsOpenWorld: false,
            IsAgentEligible: true,
            RejectionReason: null);
    }
}
