using Harness.BusinessLogic.Framework;

namespace Harness.BusinessLogic.Tests.Framework;

public sealed class FrameworkResolverTests
{
    private readonly FrameworkResolver resolver = new();

    [Fact]
    public void More_specific_rule_overrides_a_general_rule()
    {
        FrameworkResolution result = resolver.Resolve(
        [
            Rule("architecture", "layered", 0, "global", source: "global.xml"),
            Rule("architecture", "vertical-slices", 1, "repository", source: "AGENTS.md"),
        ]);

        EffectiveFrameworkRule rule = Assert.Single(result.Rules);
        Assert.Equal("vertical-slices", rule.Value);
        Assert.Equal("AGENTS.md", rule.Source);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Locked_general_rule_blocks_a_more_specific_override()
    {
        FrameworkResolution result = resolver.Resolve(
        [
            Rule(
                "network",
                "approval-required",
                0,
                "global",
                isLocked: true,
                source: "global.xml"),
            Rule("network", "allowed", 2, "workspace", source: "private-overlay"),
        ]);

        EffectiveFrameworkRule rule = Assert.Single(result.Rules);
        Assert.Equal("approval-required", rule.Value);
        Assert.True(rule.IsLocked);
        Assert.Contains(result.Issues, issue => issue.Code == "locked_override");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Same_level_conflict_is_not_resolved_silently()
    {
        FrameworkResolution result = resolver.Resolve(
        [
            Rule("testing", "xunit", 1, "repository", source: "AGENTS.md"),
            Rule("testing", "nunit", 1, "repository", source: "docs/testing.md"),
        ]);

        Assert.Empty(result.Rules);
        FrameworkIssue issue = Assert.Single(result.Issues);
        Assert.Equal("same_level_conflict", issue.Code);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Invalid_rule_is_reported_and_excluded()
    {
        FrameworkResolution result = resolver.Resolve(
        [
            Rule("", "value", 0, "global", source: "global.xml"),
            Rule("valid", "value", 0, "global", source: "global.xml"),
        ]);

        Assert.Equal("valid", Assert.Single(result.Rules).Key);
        Assert.Contains(result.Issues, issue => issue.Code == "invalid_rule");
        Assert.False(result.IsValid);
    }

    private static FrameworkRule Rule(
        string key,
        string value,
        int precedence,
        string layer,
        bool isLocked = false,
        string source = "source") =>
        new(key, value, precedence, layer, isLocked, source);
}
