namespace Harness.DataAccess.Research;

public enum DocumentationSourceClass
{
    ExactLocal,
    LocalIndex,
    Mcp,
    Web,
}

public sealed record DocumentationLibrary(string Value);

public sealed record DocumentationVersion(string Value);

public sealed record DocumentationQueryText(string Value);

public sealed record DocumentationSourceId(string Value);

public sealed record DocumentationCitation(string Value);

public sealed record DocumentationSourceQuery(
    DocumentationLibrary Library,
    DocumentationVersion? Version,
    DocumentationQueryText Query,
    int MaximumResults,
    int MaximumCharacters);

public sealed record DocumentationSourceMatch(
    DocumentationSourceId Source,
    DocumentationSourceClass SourceClass,
    string Title,
    string Content,
    DocumentationVersion? Version,
    DocumentationCitation Citation,
    DateTimeOffset RetrievedAt,
    string ContentSha256,
    bool IsExactVersion,
    bool IsStale,
    decimal Confidence);

public sealed record DocumentationSourceResult(
    DocumentationSourceId Source,
    DocumentationSourceClass SourceClass,
    IReadOnlyList<DocumentationSourceMatch> Matches,
    bool IsSufficient,
    string? ErrorCode,
    string? Error);

public enum DocumentationDisclosureClass
{
    PublicResearchTerms,
}

public interface IDocumentationSource
{
    DocumentationSourceId Id { get; }

    DocumentationSourceClass SourceClass { get; }

    ValueTask<DocumentationSourceResult> SearchAsync(
        DocumentationSourceQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentationCacheKey(
    DocumentationSourceId Source,
    DocumentationLibrary Library,
    DocumentationVersion? Version,
    DocumentationQueryText Query,
    string AdapterSchemaVersion,
    DocumentationDisclosureClass DisclosureClass);

public sealed record DocumentationCacheEntry(
    DocumentationCacheKey Key,
    DocumentationSourceResult Result,
    DateTimeOffset StoredAt);

public sealed record DocumentationCacheStatus(
    int EntryCount,
    long SizeBytes,
    DateTimeOffset? OldestEntry,
    DateTimeOffset? NewestEntry,
    string? LastFailure);

public interface IDocumentationCache
{
    ValueTask<DocumentationCacheEntry?> GetAsync(
        DocumentationCacheKey key,
        CancellationToken cancellationToken = default);

    ValueTask PutAsync(
        DocumentationCacheEntry entry,
        CancellationToken cancellationToken = default);

    ValueTask<DocumentationCacheStatus> CleanupAsync(
        DateTimeOffset retainAfter,
        CancellationToken cancellationToken = default);

    ValueTask<DocumentationCacheStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

public sealed record DocumentationIndexRoot(string Value);

public sealed record DocumentationWebEndpoint(Uri Value);

public sealed record DocumentationMcpToolRoute(string Connection, string Tool);

public enum ResearchRefreshPolicy
{
    OnDemand,
    Daily,
    Weekly,
    Manual,
}

public sealed record ResearchSourceSettings(
    bool ExactLocalEnabled,
    bool LocalIndexEnabled,
    bool McpEnabled,
    bool WebEnabled,
    bool Offline,
    IReadOnlyList<DocumentationIndexRoot> IndexRoots,
    IReadOnlyList<DocumentationMcpToolRoute> McpTools,
    IReadOnlyList<DocumentationWebEndpoint> WebEndpoints,
    IReadOnlyList<PackageSourceUri> PackageSources,
    ResearchRefreshPolicy RefreshPolicy,
    int MaximumResults,
    int MaximumCharacters,
    TimeSpan MaximumCacheAge,
    TimeSpan Retention);

public interface IResearchSettingsStore
{
    ValueTask<ResearchSourceSettings> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        ResearchSourceSettings settings,
        CancellationToken cancellationToken = default);
}

public enum DependencyOrigin
{
    Declared,
    Central,
    Direct,
    Transitive,
    Locked,
    Restored,
}

public sealed record PackageIdentity(string Value);

public sealed record PackageVersion(string Value);

public sealed record TargetFrameworkMoniker(string Value);

public sealed record RuntimeIdentifier(string Value);

public sealed record DependencyEvidencePath(string Value);

public sealed record PackageDependencyEdge(
    PackageIdentity Package,
    string VersionRange);

public sealed record PackageDependencyEvidence(
    PackageIdentity Package,
    PackageVersion? DeclaredVersion,
    PackageVersion? CentralVersion,
    PackageVersion? ResolvedVersion,
    TargetFrameworkMoniker? TargetFramework,
    RuntimeIdentifier? Runtime,
    bool IsDirect,
    IReadOnlySet<DependencyOrigin> Origins,
    IReadOnlyList<PackageDependencyEdge> Dependencies,
    string? Sha512,
    string? PackagePath,
    IReadOnlyList<DependencyEvidencePath> Evidence,
    string? DeclarationCondition = null,
    string? CentralCondition = null);

public sealed record DependencyConflict(
    PackageIdentity Package,
    string Kind,
    IReadOnlyList<string> Values,
    string Message);

public sealed record DependencyProjectEvidence(
    string ProjectPath,
    IReadOnlyList<TargetFrameworkMoniker> TargetFrameworks,
    IReadOnlyList<RuntimeIdentifier> RuntimeIdentifiers,
    IReadOnlyList<PackageDependencyEvidence> Packages,
    IReadOnlyList<DependencyConflict> Conflicts,
    bool HasRestoredAssets,
    string? ErrorCode,
    string? Error);

public sealed record DependencyEvidenceSnapshot(
    string EntryPoint,
    IReadOnlyList<DependencyProjectEvidence> Projects,
    IReadOnlyList<DependencyConflict> Conflicts,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public interface IDependencyEvidenceReader
{
    ValueTask<DependencyEvidenceSnapshot> InspectAsync(
        string workspaceRoot,
        string entryPoint,
        CancellationToken cancellationToken = default);
}

public sealed record PackageSourceUri(Uri Value);

public sealed record PackageCandidateQuery(
    PackageIdentity Package,
    PackageVersion Version,
    IReadOnlyList<TargetFrameworkMoniker> TargetFrameworks,
    IReadOnlyList<RuntimeIdentifier> RuntimeIdentifiers,
    bool AllowPrerelease,
    IReadOnlyList<PackageSourceUri> Sources);

public sealed record PackageAdvisory(
    Uri Url,
    int Severity);

public sealed record PackageAssetCompatibility(
    TargetFrameworkMoniker TargetFramework,
    bool? IsCompatible,
    IReadOnlyList<string> NearestAssetGroups);

public sealed record PackageRuntimeCompatibility(
    RuntimeIdentifier Runtime,
    bool? IsCompatible,
    IReadOnlyList<string> AvailableRuntimeGroups);

public sealed record PackageCandidateMetadata(
    PackageIdentity Package,
    PackageVersion Version,
    PackageSourceUri Source,
    bool Exists,
    bool? IsListed,
    bool IsPrerelease,
    bool? IsDeprecated,
    string? DeprecationMessage,
    string? LicenseExpression,
    Uri? LicenseUrl,
    Uri? ProjectUrl,
    Uri? RepositoryUrl,
    string? RepositoryCommit,
    string? PublishedSha512,
    string? ComputedSha512,
    IReadOnlyList<PackageDependencyEdge> Dependencies,
    IReadOnlyList<PackageAssetCompatibility> Compatibility,
    IReadOnlyList<PackageRuntimeCompatibility> RuntimeCompatibility,
    IReadOnlyList<PackageAdvisory> Advisories,
    string RegistrationCitation,
    string? ErrorCode,
    string? Error);

public interface IPackageCandidateMetadataClient
{
    ValueTask<IReadOnlyList<PackageCandidateMetadata>> GetAsync(
        PackageCandidateQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record SbomExportContent(string Json, string Sha256);

public sealed record SbomExportOutcome(
    string Path,
    string? Sha256,
    long BytesWritten,
    string? ErrorCode,
    string? Error);

public interface ISbomExporter
{
    ValueTask<SbomExportOutcome> ExportAsync(
        string path,
        SbomExportContent content,
        bool overwrite,
        CancellationToken cancellationToken = default);
}
