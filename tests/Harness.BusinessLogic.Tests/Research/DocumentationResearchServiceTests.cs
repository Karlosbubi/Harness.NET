using Harness.BusinessLogic.Research;
using Harness.DataAccess.Research;

namespace Harness.BusinessLogic.Tests.Research;

public sealed class DocumentationResearchServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T12:00:00Z");

    [Fact]
    public async Task Stops_after_exact_authoritative_source_is_sufficient()
    {
        StubSource exact = Source(DocumentationSourceClass.ExactLocal,
            Result(DocumentationSourceClass.ExactLocal, sufficient: true,
                Match(DocumentationSourceClass.ExactLocal, "exact", "A", "citation:a", true, 0.9m)));
        StubSource web = Source(DocumentationSourceClass.Web,
            Result(DocumentationSourceClass.Web, sufficient: true,
                Match(DocumentationSourceClass.Web, "web", "B", "https://example.test", true, 0.9m)));
        DocumentationResearchService service = Service([web, exact], Settings());

        DocumentationLookupResult result = await service.LookupAsync(Request());

        Assert.True(result.IsSufficient);
        Assert.Single(result.Results);
        Assert.Equal(1, exact.Calls);
        Assert.Equal(0, web.Calls);
        Assert.Equal(ResearchSourceKind.ExactLocal, result.Results[0].SourceKind);
    }

    [Fact]
    public async Task Escalates_after_local_and_mcp_failures_to_web_fallback()
    {
        StubSource exact = Source(DocumentationSourceClass.ExactLocal,
            Result(DocumentationSourceClass.ExactLocal, false));
        StubSource index = Source(DocumentationSourceClass.LocalIndex,
            new(new("localindex"), DocumentationSourceClass.LocalIndex, [], false,
                "index_failed", "index unavailable"));
        StubSource mcp = Source(DocumentationSourceClass.Mcp,
            new(new("mcp"), DocumentationSourceClass.Mcp, [], false,
                "mcp_failed", "MCP unavailable"));
        StubSource web = Source(DocumentationSourceClass.Web,
            Result(DocumentationSourceClass.Web, true,
                Match(DocumentationSourceClass.Web, "web", "Fallback", "https://docs.example.test/a",
                    true, 0.75m)));
        DocumentationResearchService service = Service([web, mcp, index, exact], Settings());

        DocumentationLookupResult result = await service.LookupAsync(Request());

        Assert.True(result.IsSufficient);
        Assert.Equal([1, 1, 1, 1], new[] { exact.Calls, index.Calls, mcp.Calls, web.Calls });
        Assert.Contains(result.Escalation, item => item.SourceKind == ResearchSourceKind.Mcp &&
            item.Action == ResearchEscalationAction.Failed);
        Assert.Equal("https://docs.example.test/a", Assert.Single(result.Results).Citation.Value);
    }

    [Fact]
    public async Task Offline_mode_uses_stale_remote_cache_without_calling_remote_source()
    {
        StubSource web = Source(DocumentationSourceClass.Web,
            Result(DocumentationSourceClass.Web, true));
        MemoryCache cache = new();
        DocumentationSourceQuery query = Query();
        DocumentationCacheKey key = new(web.Id, query.Library, query.Version, query.Query,
            "documentation-evidence-v1", DocumentationDisclosureClass.PublicResearchTerms);
        cache.Entry = new(key, Result(DocumentationSourceClass.Web, true,
            Match(DocumentationSourceClass.Web, "web", "Cached", "https://docs.example.test/cached",
                true, 0.8m)), Now.AddDays(-30));
        DocumentationResearchService service = Service([web], Settings(offline: true), cache);

        DocumentationLookupResult result = await service.LookupAsync(Request());

        Assert.Equal(0, web.Calls);
        Assert.Equal(ResearchFreshness.Stale, Assert.Single(result.Results).Freshness);
        Assert.Contains(result.Escalation, item => item.Action == ResearchEscalationAction.CacheHit);
    }

    [Fact]
    public async Task Keeps_conflicts_deduplicates_identity_and_enforces_context_limit()
    {
        DocumentationSourceMatch first = Match(DocumentationSourceClass.LocalIndex,
            "index", new string('A', 80), "doc:a", true, 0.8m);
        DocumentationSourceMatch duplicate = first;
        DocumentationSourceMatch conflict = Match(DocumentationSourceClass.LocalIndex,
            "index", new string('B', 80), "doc:b", true, 0.8m);
        StubSource index = Source(DocumentationSourceClass.LocalIndex,
            Result(DocumentationSourceClass.LocalIndex, false, first, duplicate, conflict));
        ResearchSourceSettings settings = Settings() with { MaximumCharacters = 100 };
        DocumentationResearchService service = Service([index], settings);

        DocumentationLookupResult result = await service.LookupAsync(Request());

        Assert.True(result.HasConflicts);
        Assert.Equal(2, result.Results.Count);
        Assert.True(result.Results.Sum(item => item.Content.Length) <= 100);
        Assert.Equal(2, result.Results.Select(item => item.Citation.Value).Distinct().Count());
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_source_failure()
    {
        StubSource source = new(DocumentationSourceClass.ExactLocal, (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Result(DocumentationSourceClass.ExactLocal, false));
        });
        DocumentationResearchService service = Service([source], Settings());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.LookupAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task Resolves_core_library_version_from_current_dependency_evidence_when_omitted()
    {
        DocumentationSourceQuery? observed = null;
        StubSource exact = new(DocumentationSourceClass.ExactLocal, (query, _) =>
        {
            observed = query;
            return ValueTask.FromResult(Result(DocumentationSourceClass.ExactLocal, true,
                Match(DocumentationSourceClass.ExactLocal, "exact", "Matched", "nuget:Avalonia", true,
                    0.9m)));
        });
        DocumentationResearchService service = new(
            null!, [exact], new StaticSettings(Settings()), new MemoryCache(),
            new FixedTimeProvider(Now), new DependencyVersionService());

        DocumentationLookupResult result = await service.LookupAsync(new(
            null, new("Avalonia"), null, new("binding")));

        Assert.Equal("12.1.0", observed?.Version?.Value);
        Assert.Contains(result.Escalation, item => item.Source.Value == "workspace-dependency-evidence" &&
            item.Reason.Contains("12.1.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolves_dotnet_version_from_target_framework_when_omitted()
    {
        DocumentationSourceQuery? observed = null;
        StubSource exact = new(DocumentationSourceClass.ExactLocal, (query, _) =>
        {
            observed = query;
            return ValueTask.FromResult(Result(DocumentationSourceClass.ExactLocal, true));
        });
        DocumentationResearchService service = new(
            null!, [exact], new StaticSettings(Settings()), new MemoryCache(),
            new FixedTimeProvider(Now), new DependencyVersionService(
                packageVersion: null, targetFramework: "net10.0-linux"));

        await service.LookupAsync(new(null, new(".NET"), null, new("Span<T>")));

        Assert.Equal("10.0", observed?.Version?.Value);
    }

    private static DocumentationResearchService Service(
        IEnumerable<IDocumentationSource> sources,
        ResearchSourceSettings settings,
        MemoryCache? cache = null) => new(
        workspaceResolver: null!,
        sources,
        new StaticSettings(settings),
        cache ?? new MemoryCache(),
        new FixedTimeProvider(Now));

    private static DocumentationLookupRequest Request() => new(
        null, new("Avalonia"), new("12.1.0"), new("How does compiled binding work?"));

    private static DocumentationSourceQuery Query() => new(
        new("Avalonia"), new("12.1.0"), new("How does compiled binding work?"), 5, 12_000);

    private static ResearchSourceSettings Settings(bool offline = false) => new(
        true, true, true, true, offline, [], [new("docs", "search")],
        [new(new Uri("https://learn.microsoft.com/api/search"))],
        [new(new Uri("https://api.nuget.org/v3/index.json"))],
        ResearchRefreshPolicy.OnDemand, 5, 12_000, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    private static StubSource Source(
        DocumentationSourceClass sourceClass,
        DocumentationSourceResult result) => new(sourceClass,
        (_, _) => ValueTask.FromResult(result));

    private static DocumentationSourceResult Result(
        DocumentationSourceClass sourceClass,
        bool sufficient,
        params DocumentationSourceMatch[] matches) => new(
        new(sourceClass.ToString().ToLowerInvariant()), sourceClass, matches, sufficient, null, null);

    private static DocumentationSourceMatch Match(
        DocumentationSourceClass sourceClass,
        string source,
        string content,
        string citation,
        bool exact,
        decimal confidence) => new(
        new(source), sourceClass, "Same topic", content, new("12.1.0"), new(citation), Now,
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content))), exact, false, confidence);

    private sealed class StubSource(
        DocumentationSourceClass sourceClass,
        Func<DocumentationSourceQuery, CancellationToken, ValueTask<DocumentationSourceResult>> handler)
        : IDocumentationSource
    {
        public DocumentationSourceId Id { get; } = new(sourceClass.ToString().ToLowerInvariant());
        public DocumentationSourceClass SourceClass => sourceClass;
        internal int Calls { get; private set; }

        public ValueTask<DocumentationSourceResult> SearchAsync(
            DocumentationSourceQuery query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return handler(query, cancellationToken);
        }
    }

    private sealed class StaticSettings(ResearchSourceSettings settings) : IResearchSettingsStore
    {
        public ValueTask<ResearchSourceSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(settings);

        public ValueTask SaveAsync(ResearchSourceSettings value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class MemoryCache : IDocumentationCache
    {
        internal DocumentationCacheEntry? Entry { get; set; }

        public ValueTask<DocumentationCacheEntry?> GetAsync(DocumentationCacheKey key,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Entry?.Key == key ? Entry : null);

        public ValueTask PutAsync(DocumentationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entry = entry;
            return ValueTask.CompletedTask;
        }

        public ValueTask<DocumentationCacheStatus> CleanupAsync(DateTimeOffset retainAfter,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DocumentationCacheStatus(0, 0, null, null, null));

        public ValueTask<DocumentationCacheStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DocumentationCacheStatus(0, 0, null, null, null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DependencyVersionService(
        string? packageVersion = "12.1.0",
        string targetFramework = "net10.0") : IDependencyResearchService
    {
        public ValueTask<DependencyInspectionResult> InspectAsync(DependencyInspectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new DependencyInspectionResult(
            "App.slnx",
            [new("App.csproj", [new(targetFramework)], [], packageVersion is null ? [] :
                [new(new("Avalonia"), null, new(packageVersion), new(packageVersion),
                    new(targetFramework), null, true, new HashSet<DependencyEvidenceOrigin>(), [],
                    null, null, [])],
                [], true, null, null)], [], false, null, null));

        public ValueTask<PackageCandidateValidationResult> ValidateCandidateAsync(
            PackageCandidateValidationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SbomPreviewResult> PreviewSbomAsync(SbomPreviewRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PackageChangePreviewResult> PreviewPackageChangeAsync(
            PackageChangePreviewRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<SbomExportResult> ExportSbomAsync(SbomExportRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
