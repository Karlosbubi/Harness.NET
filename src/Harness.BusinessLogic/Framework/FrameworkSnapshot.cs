namespace Harness.BusinessLogic.Framework;

public sealed record FrameworkSnapshot(
    IReadOnlyList<FrameworkDocumentView> Documents,
    IReadOnlyList<EffectiveFrameworkRule> Rules,
    IReadOnlyList<FrameworkIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Code != "invalid_rule" &&
                                              issue.Code != "same_level_conflict" &&
                                              issue.Code != "source_error");
}
