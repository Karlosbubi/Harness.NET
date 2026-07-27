namespace Harness.DataAccess.Framework;

public sealed record FrameworkSourceResult(
    IReadOnlyList<FrameworkDocument> Documents,
    IReadOnlyList<string> Errors);
