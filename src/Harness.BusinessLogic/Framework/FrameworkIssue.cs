namespace Harness.BusinessLogic.Framework;

public sealed record FrameworkIssue(
    string Code,
    string Message,
    string? Key,
    IReadOnlyList<string> Sources);
