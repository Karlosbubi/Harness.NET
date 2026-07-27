namespace Harness.DataAccess.Inspection;

public sealed record DotNetReferenceInfo(
    string Kind,
    string Identity,
    string? Version);
