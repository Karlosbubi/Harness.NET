namespace Harness.BusinessLogic.Framework;

public sealed record FrameworkResolution(
    IReadOnlyList<EffectiveFrameworkRule> Rules,
    IReadOnlyList<FrameworkIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Code != "invalid_rule" &&
                                              issue.Code != "same_level_conflict");
}
