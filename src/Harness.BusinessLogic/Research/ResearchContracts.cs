using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Research;

public enum ResearchSourceKind
{
    ExactLocal,
    LocalIndex,
    Mcp,
    Web,
}

public enum ResearchFreshness
{
    Fresh,
    Stale,
    Unknown,
}

public enum ResearchConfidence
{
    Low,
    Medium,
    High,
}

public enum ResearchEscalationAction
{
    Queried,
    CacheHit,
    Skipped,
    Failed,
    Insufficient,
    Sufficient,
}

public sealed record ResearchLibraryName(string Value);

public sealed record ResearchLibraryVersion(string Value);

public sealed record ResearchQuestion(string Value);

public sealed record ResearchCitation(string Value);

public sealed record ResearchSourceName(string Value);

public sealed record DocumentationLookupRequest(
    GoalId? GoalId,
    ResearchLibraryName Library,
    ResearchLibraryVersion? Version,
    ResearchQuestion Question);

public sealed record DocumentationEvidenceView(
    ResearchSourceName Source,
    ResearchSourceKind SourceKind,
    string Title,
    string Content,
    ResearchLibraryVersion? Version,
    ResearchFreshness Freshness,
    ResearchConfidence Confidence,
    ResearchCitation Citation,
    DateTimeOffset RetrievedAt,
    int Rank,
    bool IsExactVersion);

public sealed record ResearchEscalationView(
    ResearchSourceName Source,
    ResearchSourceKind SourceKind,
    ResearchEscalationAction Action,
    string Reason);

public sealed record DocumentationLookupResult(
    ResearchLibraryName Library,
    ResearchLibraryVersion? RequestedVersion,
    IReadOnlyList<DocumentationEvidenceView> Results,
    IReadOnlyList<ResearchEscalationView> Escalation,
    bool IsSufficient,
    bool HasConflicts,
    string? ErrorCode,
    string? Error);

public interface IDocumentationResearchService
{
    ValueTask<DocumentationLookupResult> LookupAsync(
        DocumentationLookupRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentationLibraryCatalogEntry(
    ResearchLibraryName Name,
    IReadOnlyList<string> PackageIds,
    string VersionSource);

public sealed record DocumentationLibraryCatalog(
    IReadOnlyList<DocumentationLibraryCatalogEntry> Entries)
{
    public static DocumentationLibraryCatalog Core { get; } = new(
    [
        new(new(".NET"), ["Microsoft.NETCore.App.Ref"], "selected SDK/reference pack"),
        new(new("Avalonia"), ["Avalonia", "Avalonia.Desktop"], "restored package"),
        new(new("Rx.NET"), ["System.Reactive"], "restored package"),
        new(new("Serilog"), ["Serilog"], "restored package"),
        new(new("Microsoft Agent Framework"), ["Microsoft.Agents.AI"], "restored package"),
        new(new("Roslyn"), ["Microsoft.CodeAnalysis.Common", "Microsoft.CodeAnalysis.CSharp"],
            "restored package"),
        new(new("Dock"), ["Dock.Avalonia", "Dock.Model.Avalonia"], "restored package"),
        new(new("Dapper"), ["Dapper"], "restored package"),
        new(new("SQLite"), ["Microsoft.Data.Sqlite"], "restored package"),
        new(new("xUnit"), ["xunit", "xunit.v3.core"], "restored package"),
    ]);
}

public enum DependencyEvidenceOrigin
{
    Declared,
    Central,
    Direct,
    Transitive,
    Locked,
    Restored,
}

public sealed record DependencyPackageId(string Value);

public sealed record DependencyPackageVersion(string Value);

public sealed record DependencyTargetFramework(string Value);

public sealed record DependencyRuntime(string Value);

public sealed record DependencyEdgeView(
    DependencyPackageId Package,
    string VersionRange);

public sealed record DependencyPackageView(
    DependencyPackageId Package,
    DependencyPackageVersion? DeclaredVersion,
    DependencyPackageVersion? CentralVersion,
    DependencyPackageVersion? ResolvedVersion,
    DependencyTargetFramework? TargetFramework,
    DependencyRuntime? Runtime,
    bool IsDirect,
    IReadOnlySet<DependencyEvidenceOrigin> Origins,
    IReadOnlyList<DependencyEdgeView> Dependencies,
    string? Sha512,
    string? PackagePath,
    IReadOnlyList<string> EvidencePaths,
    string? DeclarationCondition = null,
    string? CentralCondition = null);

public sealed record DependencyConflictView(
    DependencyPackageId Package,
    string Kind,
    IReadOnlyList<string> Values,
    string Message);

public sealed record DependencyProjectView(
    string ProjectPath,
    IReadOnlyList<DependencyTargetFramework> TargetFrameworks,
    IReadOnlyList<DependencyRuntime> RuntimeIdentifiers,
    IReadOnlyList<DependencyPackageView> Packages,
    IReadOnlyList<DependencyConflictView> Conflicts,
    bool HasRestoredAssets,
    string? ErrorCode,
    string? Error);

public enum DependencyInspectionScope
{
    Original,
    ApprovedWorktree,
}

public sealed record DependencyInspectionRequest(
    GoalId? GoalId,
    DependencyInspectionScope Scope = DependencyInspectionScope.Original);

public sealed record DependencyInspectionResult(
    string EntryPoint,
    IReadOnlyList<DependencyProjectView> Projects,
    IReadOnlyList<DependencyConflictView> Conflicts,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public enum PackageCandidateDecision
{
    Accepted,
    ReviewRequired,
    Rejected,
}

public sealed record PackageCandidateValidationRequest(
    GoalId? GoalId,
    DependencyPackageId Package,
    DependencyPackageVersion Version,
    bool AllowPrerelease,
    DependencyInspectionScope Scope = DependencyInspectionScope.Original);

public sealed record PackageSourceEvidenceView(
    string Source,
    bool Exists,
    bool? IsListed,
    bool IsPrerelease,
    bool? IsDeprecated,
    string? DeprecationMessage,
    string? License,
    string? ProjectUrl,
    string? RepositoryUrl,
    string? RepositoryCommit,
    string? PublishedSha512,
    string? ComputedSha512,
    IReadOnlyList<DependencyEdgeView> Dependencies,
    IReadOnlyList<string> Compatibility,
    IReadOnlyList<string> Advisories,
    ResearchCitation Citation,
    string? ErrorCode,
    string? Error);

public sealed record PackageCandidateValidationResult(
    DependencyPackageId Package,
    DependencyPackageVersion Version,
    PackageCandidateDecision Decision,
    IReadOnlyList<string> Findings,
    IReadOnlyList<PackageSourceEvidenceView> Sources,
    string? ErrorCode,
    string? Error);

public sealed record SbomDocument(string Format, string Json, string Sha256);

public sealed record SbomPreviewRequest(
    GoalId? GoalId,
    DependencyInspectionScope Scope = DependencyInspectionScope.Original);

public sealed record SbomPreviewResult(
    DependencyInspectionResult Dependencies,
    SbomDocument? Sbom,
    string? ErrorCode,
    string? Error);

public sealed record PackageChangePreviewRequest(
    GoalId? GoalId,
    DependencyPackageId Package,
    DependencyPackageVersion Version,
    bool AllowPrerelease,
    DependencyInspectionScope Scope = DependencyInspectionScope.Original);

public sealed record PackageChangePreviewResult(
    PackageCandidateValidationResult Validation,
    string DependencyDiff,
    string SbomDiff,
    SbomDocument? CurrentSbom,
    SbomDocument? ProposedSbom,
    string? ErrorCode,
    string? Error);

public sealed record SbomExportPath(string Value);

public sealed record SbomExportRequest(
    GoalId? GoalId,
    SbomExportPath Path,
    bool Overwrite,
    DependencyInspectionScope Scope = DependencyInspectionScope.Original);

public sealed record SbomExportResult(
    SbomExportPath Path,
    string? Sha256,
    long BytesWritten,
    string? ErrorCode,
    string? Error);

public interface IDependencyResearchService
{
    ValueTask<DependencyInspectionResult> InspectAsync(
        DependencyInspectionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PackageCandidateValidationResult> ValidateCandidateAsync(
        PackageCandidateValidationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<SbomPreviewResult> PreviewSbomAsync(
        SbomPreviewRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PackageChangePreviewResult> PreviewPackageChangeAsync(
        PackageChangePreviewRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<SbomExportResult> ExportSbomAsync(
        SbomExportRequest request,
        CancellationToken cancellationToken = default);
}

public enum ResearchRefreshMode
{
    OnDemand,
    Daily,
    Weekly,
    Manual,
}

public sealed record ResearchSettingsSnapshot(
    bool ExactLocalEnabled,
    bool LocalIndexEnabled,
    bool McpEnabled,
    bool WebEnabled,
    bool Offline,
    IReadOnlyList<string> IndexRoots,
    IReadOnlyList<string> McpDocumentationTools,
    IReadOnlyList<string> WebEndpoints,
    IReadOnlyList<string> PackageSources,
    ResearchRefreshMode RefreshMode,
    int MaximumResults,
    int MaximumCharacters,
    int MaximumCacheAgeHours,
    int RetentionDays,
    int CacheEntries,
    long CacheBytes,
    string? LastCacheFailure);

public sealed record ResearchSettingsUpdate(
    bool ExactLocalEnabled,
    bool LocalIndexEnabled,
    bool McpEnabled,
    bool WebEnabled,
    bool Offline,
    IReadOnlyList<string> IndexRoots,
    IReadOnlyList<string> McpDocumentationTools,
    IReadOnlyList<string> WebEndpoints,
    IReadOnlyList<string> PackageSources,
    ResearchRefreshMode RefreshMode,
    int MaximumResults,
    int MaximumCharacters,
    int MaximumCacheAgeHours,
    int RetentionDays);

public sealed record ResearchSettingsResult(
    ResearchSettingsSnapshot? Snapshot,
    string? ErrorCode,
    string? Error);

public interface IResearchSettingsService
{
    ValueTask<ResearchSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    ValueTask<ResearchSettingsResult> SaveAsync(
        ResearchSettingsUpdate update,
        CancellationToken cancellationToken = default);

    ValueTask<ResearchSettingsSnapshot> CleanupCacheAsync(
        CancellationToken cancellationToken = default);
}
