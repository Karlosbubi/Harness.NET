namespace Harness.BusinessLogic.Inspection;

public sealed record DotNetReferenceView(
    string Kind,
    string Identity,
    string? Version);
