using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.CodeIntelligence;

namespace Harness.DataAccess.Tests.CodeIntelligence;

[Collection("Roslyn workspace compatibility")]
public sealed class RoslynCodeIntelligenceEngineTests : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-roslyn-engine-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Loads_with_progress_and_reports_version_matched_compiler_diagnostics()
    {
        const string original = "class Sample { void Run() { } }\n";
        await CreateProjectAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        ProgressCollector progress = new();
        CodeIntelligenceContextId contextId = new("context-1");

        CodeIntelligenceSessionResult session = await engine.OpenAsync(
            OpenRequest(contextId),
            progress);
        CodeIntelligenceDiagnosticResult diagnostics = await engine.GetDiagnosticsAsync(new(
            contextId,
            session.SessionId!,
            new("Sample.cs"),
            new(Hash(original)),
            new(7),
            new("class Sample { void Run() { int value = ; } }\n")));

        Assert.NotEqual(CodeIntelligenceResultState.Failed, session.State);
        Assert.Equal(CodeIntelligenceLoadStage.SelectingSdk, progress.Values[0].Stage);
        Assert.Equal(CodeIntelligenceLoadStage.Ready, progress.Values[^1].Stage);
        Assert.Equal(new CodeIntelligenceBufferVersion(7), diagnostics.BufferVersion);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Severity == CodeIntelligenceDiagnosticSeverity.Error &&
            diagnostic.Id.Value.StartsWith("CS", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(root, "obj", "project.assets.json")));
    }

    [Fact]
    public async Task Changed_persisted_baseline_is_rejected_as_stale()
    {
        const string original = "class Sample { }\n";
        await CreateProjectAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        await File.WriteAllTextAsync(
            Path.Combine(root, "Sample.cs"),
            "class Sample { int Changed; }\n",
            Utf8WithoutBom);

        CodeIntelligenceDiagnosticResult result = await engine.GetDiagnosticsAsync(new(
            contextId,
            session.SessionId!,
            new("Sample.cs"),
            new(Hash(original)),
            new(1),
            new(original)));

        Assert.Equal(CodeIntelligenceResultState.Stale, result.State);
        Assert.Equal("baseline_changed", Assert.Single(result.Issues).Code.Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Replacing_the_foreground_context_invalidates_the_previous_session()
    {
        const string original = "class Sample { }\n";
        await CreateProjectAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId firstContext = new("context-1");
        CodeIntelligenceSessionResult first = await engine.OpenAsync(OpenRequest(firstContext));
        CodeIntelligenceContextId secondContext = new("context-2");
        CodeIntelligenceSessionResult second = await engine.OpenAsync(OpenRequest(secondContext));

        CodeIntelligenceDiagnosticResult stale = await engine.GetDiagnosticsAsync(new(
            firstContext,
            first.SessionId!,
            new("Sample.cs"),
            new(Hash(original)),
            new(1),
            new(original)));

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(CodeIntelligenceResultState.Stale, stale.State);
        Assert.Equal("session_unavailable", Assert.Single(stale.Issues).Code.Value);
    }

    [Fact]
    public async Task Invalid_project_returns_an_actionable_degraded_state()
    {
        await CreateProjectAsync("class Sample { }\n");
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), "<Project");
        using RoslynCodeIntelligenceEngine engine = CreateEngine();

        CodeIntelligenceSessionResult result = await engine.OpenAsync(
            OpenRequest(new("context-1")));

        Assert.Equal(CodeIntelligenceResultState.Degraded, result.State);
        Assert.NotEmpty(result.Issues);
        Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message.Value)));
    }

    private async ValueTask CreateProjectAsync(string source)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), """
            {
              "sdk": {
                "version": "10.0.201",
                "rollForward": "latestPatch",
                "allowPrerelease": false
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Sample.cs"),
            source,
            Utf8WithoutBom);
    }

    private RoslynCodeIntelligenceEngine CreateEngine() => new(
        new MSBuildRuntime(new(new DotNetProcess())));

    private CodeIntelligenceOpenRequest OpenRequest(CodeIntelligenceContextId contextId) => new(
        contextId,
        new(root),
        new("Sample.csproj"),
        CodeIntelligenceSourceKind.ApprovedGoalWorktree);

    private static string Hash(string content) => Convert.ToHexStringLower(
        SHA256.HashData(Utf8WithoutBom.GetBytes(content)));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ProgressCollector : IProgress<CodeIntelligenceLoadProgress>
    {
        internal List<CodeIntelligenceLoadProgress> Values { get; } = [];

        public void Report(CodeIntelligenceLoadProgress value) => Values.Add(value);
    }
}
