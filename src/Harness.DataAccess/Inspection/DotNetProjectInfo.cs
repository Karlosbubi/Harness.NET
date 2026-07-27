namespace Harness.DataAccess.Inspection;

public sealed record DotNetProjectInfo(
    string Path,
    string? Sdk,
    IReadOnlyList<string> TargetFrameworks,
    string? LanguageVersion,
    string? Nullable,
    IReadOnlyList<DotNetReferenceInfo> References);
