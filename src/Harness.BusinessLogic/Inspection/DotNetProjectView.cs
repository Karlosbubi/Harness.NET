namespace Harness.BusinessLogic.Inspection;

public sealed record DotNetProjectView(
    string Path,
    string? Sdk,
    IReadOnlyList<string> TargetFrameworks,
    string? LanguageVersion,
    string? Nullable,
    IReadOnlyList<DotNetReferenceView> References,
    DotNetProjectDetailsView? Details = null);
