using System.Xml.Linq;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Research;

namespace Harness.DataAccess.Tests.Research;

public sealed class ResearchStorageAndLocalDocumentationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"harness-research-{Guid.NewGuid():N}");

    [Fact]
    public async Task Settings_round_trip_preserves_unrelated_private_configuration()
    {
        StubPaths paths = new(Paths());
        Directory.CreateDirectory(paths.Current.ConfigDirectory);
        string configuration = Path.Combine(paths.Current.ConfigDirectory, "harness.xml");
        await File.WriteAllTextAsync(configuration,
            "<Harness><Routing><MainLlm>Ollama</MainLlm></Routing></Harness>");
        XdgResearchSettingsStore store = new(paths);
        ResearchSourceSettings value = new(
            true, true, true, true, false,
            [new(Path.Combine(root, "docs"))],
            [new("docs", "search")],
            [new(new Uri("https://learn.microsoft.com/api/search"))],
            [new(new Uri("https://api.nuget.org/v3/index.json"))],
            ResearchRefreshPolicy.Weekly,
            7,
            24_000,
            TimeSpan.FromHours(12),
            TimeSpan.FromDays(45));

        await store.SaveAsync(value);
        ResearchSourceSettings read = await store.GetAsync();

        Assert.Equal(value.ExactLocalEnabled, read.ExactLocalEnabled);
        Assert.Equal(value.LocalIndexEnabled, read.LocalIndexEnabled);
        Assert.Equal(value.McpEnabled, read.McpEnabled);
        Assert.Equal(value.WebEnabled, read.WebEnabled);
        Assert.Equal(value.Offline, read.Offline);
        Assert.Equal(value.IndexRoots, read.IndexRoots);
        Assert.Equal(value.McpTools, read.McpTools);
        Assert.Equal(value.WebEndpoints, read.WebEndpoints);
        Assert.Equal(value.PackageSources, read.PackageSources);
        Assert.Equal(value.RefreshPolicy, read.RefreshPolicy);
        Assert.Equal(value.MaximumResults, read.MaximumResults);
        Assert.Equal(value.MaximumCharacters, read.MaximumCharacters);
        Assert.Equal(value.MaximumCacheAge, read.MaximumCacheAge);
        Assert.Equal(value.Retention, read.Retention);
        XDocument document = XDocument.Load(configuration);
        Assert.Equal("Ollama", document.Root?.Element("Routing")?.Element("MainLlm")?.Value);
        Assert.Equal("docs", document.Root?.Element("Research")?.Element("McpTools")?
            .Element("Tool")?.Attribute("Connection")?.Value);
    }

    [Fact]
    public async Task Cache_uses_full_identity_and_retention_removes_old_entries()
    {
        StubPaths paths = new(Paths());
        FileDocumentationCache cache = new(paths);
        DocumentationCacheKey key = new(new("source"), new("Avalonia"), new("12.1.0"),
            new("binding"), "v1", DocumentationDisclosureClass.PublicResearchTerms);
        DocumentationSourceResult source = new(new("source"), DocumentationSourceClass.LocalIndex,
            [new(new("source"), DocumentationSourceClass.LocalIndex, "Binding", "Content",
                new("12.1.0"), new("doc:test"), DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
                "hash", true, false, 0.8m)], true, null, null);
        await cache.PutAsync(new(key, source, DateTimeOffset.Parse("2026-08-11T00:00:00Z")));

        Assert.NotNull(await cache.GetAsync(key));
        Assert.Null(await cache.GetAsync(key with { Query = new("different") }));
        string file = Assert.Single(Directory.GetFiles(Path.Combine(paths.Current.CacheDirectory,
            "documentation"), "*.json"));
        File.SetLastWriteTimeUtc(file, DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        DocumentationCacheStatus status = await cache.CleanupAsync(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        Assert.Equal(0, status.EntryCount);
    }

    [Fact]
    public async Task Local_index_marks_only_path_version_matches_as_exact_and_bounds_content()
    {
        string docs = Path.Combine(root, "docs");
        string exact = Path.Combine(docs, "Avalonia", "12.1.0");
        string other = Path.Combine(docs, "Avalonia", "11.0.0");
        Directory.CreateDirectory(exact);
        Directory.CreateDirectory(other);
        await File.WriteAllTextAsync(Path.Combine(exact, "binding.md"),
            "# Binding\n\nCompiled binding uses x:DataType and reports compile-time failures.");
        await File.WriteAllTextAsync(Path.Combine(other, "binding.md"),
            "# Binding\n\nOld binding guidance also mentions x:DataType.");
        StaticSettings settings = new(Settings(docs));
        LocalDocumentationIndexSource source = new(settings,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-11T12:00:00Z")));

        DocumentationSourceResult result = await source.SearchAsync(new(
            new("Avalonia"), new("12.1.0"), new("x:DataType binding"), 2, 80));

        Assert.True(result.IsSufficient);
        Assert.Equal(2, result.Matches.Count);
        Assert.True(result.Matches[0].IsExactVersion);
        Assert.True(result.Matches.Sum(match => match.Content.Length) <= 80);
        Assert.All(result.Matches, match => Assert.StartsWith("doc-index:", match.Citation.Value));
    }

    private ResearchSourceSettings Settings(string docs) => new(
        true, true, true, true, false,
        [new(docs)], [], [], [new(new Uri("https://api.nuget.org/v3/index.json"))],
        ResearchRefreshPolicy.OnDemand, 5, 12_000, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    private ApplicationPaths Paths() => new(
        Path.Combine(root, "config"), Path.Combine(root, "data"), Path.Combine(root, "state"),
        Path.Combine(root, "cache"), Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"), Path.Combine(root, "state", "worktrees"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }

    private sealed class StaticSettings(ResearchSourceSettings value) : IResearchSettingsStore
    {
        public ValueTask<ResearchSourceSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(value);

        public ValueTask SaveAsync(ResearchSourceSettings settings,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
