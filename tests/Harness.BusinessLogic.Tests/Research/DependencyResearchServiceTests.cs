using System.Text.Json;
using Harness.BusinessLogic.Research;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Research;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Tests.Research;

public sealed class DependencyResearchServiceTests
{
    [Fact]
    public async Task Sbom_is_byte_reproducible_sorted_and_has_no_volatile_identity()
    {
        FakeExporter exporter = new();
        DependencyResearchService service = Service(Graph(), [Candidate()], exporter: exporter);

        SbomPreviewResult first = await service.PreviewSbomAsync(new(null));
        SbomPreviewResult second = await service.PreviewSbomAsync(new(null));

        Assert.NotNull(first.Sbom);
        Assert.Equal(first.Sbom, second.Sbom);
        Assert.DoesNotContain("timestamp", first.Sbom.Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serialNumber", first.Sbom.Json, StringComparison.OrdinalIgnoreCase);
        Assert.True(first.Sbom.Json.IndexOf("Dapper", StringComparison.Ordinal) <
            first.Sbom.Json.IndexOf("Serilog", StringComparison.Ordinal));
        Assert.Contains("CycloneDX", first.Sbom.Json, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(first.Sbom.Json);
        JsonElement hashes = document.RootElement.GetProperty("components")[0]
            .GetProperty("hashes");
        Assert.Equal("SHA-512", hashes[0].GetProperty("alg").GetString());
        string hash = Assert.IsType<string>(hashes[0].GetProperty("content").GetString());
        Assert.Equal(128, hash.Length);
        Assert.All(hash, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal(0, exporter.Calls);
    }

    [Fact]
    public async Task Candidate_policy_rejects_advisory_and_integrity_conflict()
    {
        PackageCandidateMetadata unsafeCandidate = Candidate() with
        {
            PublishedSha512 = "published",
            ComputedSha512 = "different",
            Advisories = [new(new Uri("https://advisories.example.test/1"), 3)],
        };
        DependencyResearchService service = Service(Graph(), [unsafeCandidate]);

        PackageCandidateValidationResult result = await service.ValidateCandidateAsync(new(
            null, new("Serilog"), new("4.5.0"), false));

        Assert.Equal(PackageCandidateDecision.Rejected, result.Decision);
        Assert.Contains(result.Findings, finding => finding.Contains("advisory", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Contains("SHA-512", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Candidate_policy_keeps_registry_silence_as_review_required()
    {
        DependencyResearchService service = Service(Graph(), [Candidate()]);

        PackageCandidateValidationResult result = await service.ValidateCandidateAsync(new(
            null, new("Serilog"), new("4.5.0"), false));

        Assert.Equal(PackageCandidateDecision.ReviewRequired, result.Decision);
        Assert.Contains(result.Findings, finding => finding.Contains(
            "not proof", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Package_preview_contains_dependency_and_sbom_diffs_without_export_or_mutation()
    {
        FakeExporter exporter = new();
        DependencyResearchService service = Service(Graph(), [Candidate()], exporter: exporter);

        PackageChangePreviewResult result = await service.PreviewPackageChangeAsync(new(
            null, new("Serilog"), new("4.5.0"), false));

        Assert.Null(result.ErrorCode);
        Assert.Contains("- Serilog 4.4.0", result.DependencyDiff, StringComparison.Ordinal);
        Assert.Contains("+ Serilog 4.5.0", result.DependencyDiff, StringComparison.Ordinal);
        Assert.Contains("+ transitive System.Diagnostics.DiagnosticSource", result.DependencyDiff,
            StringComparison.Ordinal);
        Assert.NotEmpty(result.SbomDiff);
        Assert.Equal(0, exporter.Calls);
    }

    [Fact]
    public async Task Export_occurs_only_through_explicit_export_operation()
    {
        FakeExporter exporter = new();
        DependencyResearchService service = Service(Graph(), [Candidate()], exporter: exporter);

        await service.PreviewSbomAsync(new(null));
        Assert.Equal(0, exporter.Calls);
        SbomExportResult result = await service.ExportSbomAsync(new(
            null, new("/tmp/test-bom.json"), Overwrite: false));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, exporter.Calls);
        Assert.Equal("/tmp/test-bom.json", exporter.Path);
    }

    [Fact]
    public async Task Offline_candidate_validation_does_not_contact_package_source()
    {
        FakeMetadataClient metadata = new([Candidate()]);
        DependencyResearchService service = Service(Graph(), [], metadata: metadata, offline: true);

        PackageCandidateValidationResult result = await service.ValidateCandidateAsync(new(
            null, new("Serilog"), new("4.5.0"), false));

        Assert.Equal("offline_package_validation", result.ErrorCode);
        Assert.Equal(0, metadata.Calls);
    }

    [Fact]
    public async Task Sbom_requires_existing_restored_graph_for_every_project()
    {
        DependencyEvidenceSnapshot graph = Graph() with
        {
            Projects = [Graph().Projects[0] with { HasRestoredAssets = false }],
        };
        DependencyResearchService service = Service(graph, [Candidate()]);

        SbomPreviewResult result = await service.PreviewSbomAsync(new(null));

        Assert.Equal("restored_graph_incomplete", result.ErrorCode);
        Assert.Null(result.Sbom);
    }

    [Fact]
    public async Task Inspection_preserves_project_and_central_package_conditions()
    {
        DependencyResearchService service = Service(Graph(), [Candidate()]);

        DependencyInspectionResult result = await service.InspectAsync(new(null));

        DependencyPackageView package = Assert.Single(result.Projects[0].Packages,
            item => item.Package.Value == "Serilog");
        Assert.Equal("'$(TargetFramework)' == 'net10.0'", package.DeclarationCondition);
        Assert.Equal("'$(Configuration)' == 'Debug'", package.CentralCondition);
    }

    private static DependencyResearchService Service(
        DependencyEvidenceSnapshot graph,
        IReadOnlyList<PackageCandidateMetadata> candidates,
        FakeExporter? exporter = null,
        FakeMetadataClient? metadata = null,
        bool offline = false)
    {
        RegisteredWorkspace workspace = new(
            "workspace", "/workspace", "Workspace", "/workspace/App.slnx", true, true,
            "main", false, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        ResearchWorkspaceResolver resolver = new(new FakeWorkspaceStore(workspace), goalStore: null!);
        return new(
            resolver,
            new FakeEvidenceReader(graph),
            metadata ?? new FakeMetadataClient(candidates),
            new StaticSettings(Settings(offline)),
            exporter ?? new FakeExporter());
    }

    private static DependencyEvidenceSnapshot Graph()
    {
        PackageDependencyEvidence serilog = new(
            new("Serilog"), new("4.4.0"), new("4.4.0"), new("4.4.0"), new("net10.0"),
            null, true,
            new HashSet<DependencyOrigin>
            {
                DependencyOrigin.Declared, DependencyOrigin.Central,
                DependencyOrigin.Direct, DependencyOrigin.Restored,
            },
            [new(new("System.Diagnostics.DiagnosticSource"), "10.0.0")],
            Sha512(1), "serilog/4.4.0",
            [new("App.csproj"), new("obj/project.assets.json")],
            "'$(TargetFramework)' == 'net10.0'", "'$(Configuration)' == 'Debug'");
        PackageDependencyEvidence diagnostic = new(
            new("System.Diagnostics.DiagnosticSource"), null, null, new("10.0.0"), new("net10.0"),
            null, false,
            new HashSet<DependencyOrigin> { DependencyOrigin.Transitive, DependencyOrigin.Restored },
            [], Sha512(2), "system.diagnostics.diagnosticsource/10.0.0",
            [new("obj/project.assets.json")]);
        PackageDependencyEvidence dapper = new(
            new("Dapper"), null, new("2.1.79"), new("2.1.79"), new("net10.0"), null, true,
            new HashSet<DependencyOrigin>
            {
                DependencyOrigin.Declared, DependencyOrigin.Central,
                DependencyOrigin.Direct, DependencyOrigin.Restored,
            }, [], Sha512(3), "dapper/2.1.79",
            [new("App.csproj"), new("obj/project.assets.json")]);
        return new("App.slnx",
            [new("App.csproj", [new("net10.0")], [new("linux-x64")],
                [serilog, diagnostic, dapper], [], true, null, null)],
            [], false, null, null);
    }

    private static string Sha512(byte value) => Convert.ToBase64String(
        Enumerable.Repeat(value, 64).ToArray());

    private static PackageCandidateMetadata Candidate() => new(
        new("Serilog"), new("4.5.0"),
        new(new Uri("https://api.nuget.org/v3/index.json")),
        true, true, false, false, null, "Apache-2.0", null,
        new Uri("https://serilog.net"), new Uri("https://github.com/serilog/serilog"), "abc",
        "same-hash", "same-hash",
        [new(new("System.Diagnostics.DiagnosticSource"), "[10.0.0,)")],
        [new(new("net10.0"), true, ["net10.0"])], [],
        [],
        "https://api.nuget.org/registration/serilog/4.5.0.json", null, null);

    private static ResearchSourceSettings Settings(bool offline) => new(
        true, true, true, true, offline, [], [], [],
        [new(new Uri("https://api.nuget.org/v3/index.json"))],
        ResearchRefreshPolicy.OnDemand, 5, 12_000, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    private sealed class FakeEvidenceReader(DependencyEvidenceSnapshot result)
        : IDependencyEvidenceReader
    {
        public ValueTask<DependencyEvidenceSnapshot> InspectAsync(string workspaceRoot,
            string entryPoint, CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }

    private sealed class FakeMetadataClient(IReadOnlyList<PackageCandidateMetadata> result)
        : IPackageCandidateMetadataClient
    {
        internal int Calls { get; private set; }

        public ValueTask<IReadOnlyList<PackageCandidateMetadata>> GetAsync(PackageCandidateQuery query,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StaticSettings(ResearchSourceSettings result) : IResearchSettingsStore
    {
        public ValueTask<ResearchSourceSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);

        public ValueTask SaveAsync(ResearchSourceSettings settings,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeExporter : ISbomExporter
    {
        internal int Calls { get; private set; }
        internal string? Path { get; private set; }

        public ValueTask<SbomExportOutcome> ExportAsync(string path, SbomExportContent content,
            bool overwrite, CancellationToken cancellationToken = default)
        {
            Calls++;
            Path = path;
            return ValueTask.FromResult(new SbomExportOutcome(path, content.Sha256,
                System.Text.Encoding.UTF8.GetByteCount(content.Json), null, null));
        }
    }

    private sealed class FakeWorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(workspace);

        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection, string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
