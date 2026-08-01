using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.CodeIntelligence;
using Xunit.Abstractions;

namespace Harness.DataAccess.Tests.CodeIntelligence;

[Collection("Roslyn workspace compatibility")]
public sealed class RoslynCodeIntelligenceEngineTests(ITestOutputHelper output) : IDisposable
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
    public async Task Candidate_validation_rejects_an_introduced_compiler_error_without_writing()
    {
        const string original = "class Sample { void Run() { } }\n";
        const string candidate = "class Sample { void Run() { int value = ; } }\n";
        await CreateProjectAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceValidationResult result = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Candidate,
            [new(new("Sample.cs"), new(Hash(original)), new(candidate))]));

        Assert.Equal(CodeIntelligenceValidationDisposition.Rejected, result.Disposition);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
            diagnostic.Diagnostic.Source.Value == "Compiler" &&
            diagnostic.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error);
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Candidate_validation_preserves_existing_errors_and_reports_warning_evidence()
    {
        const string original = "class Sample { Missing value; }\n";
        const string candidate = "class Sample { Missing value; void Run() { int unused = 1; } }\n";
        await CreateProjectAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceValidationResult result = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Candidate,
            [new(new("Sample.cs"), new(Hash(original)), new(candidate))]));

        Assert.Equal(CodeIntelligenceValidationDisposition.Validated, result.Disposition);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Kind is CodeIntelligenceDiagnosticDeltaKind.Retained &&
            diagnostic.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
            diagnostic.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Applied_validation_requires_the_persisted_candidate_and_updates_the_session()
    {
        const string original = "class Sample { }\n";
        const string candidate = "class Sample { int Value { get; } }\n";
        await CreateProjectAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        _ = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Candidate,
            [new(new("Sample.cs"), new(Hash(original)), new(candidate))]));
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.cs"), candidate, Utf8WithoutBom);

        CodeIntelligenceValidationResult mismatch = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Applied,
            [new(new("Sample.cs"), new(Hash(candidate)), new(original))]));
        CodeIntelligenceValidationResult applied = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Applied,
            [new(new("Sample.cs"), new(Hash(candidate)), new(candidate))]));

        Assert.Equal(CodeIntelligenceResultState.Stale, mismatch.State);
        Assert.Equal("applied_content_mismatch", Assert.Single(mismatch.Issues).Code.Value);
        Assert.Equal(CodeIntelligenceValidationDisposition.Validated, applied.Disposition);
    }

    [Fact]
    public async Task Unsupported_file_validation_is_explicitly_not_applicable()
    {
        const string original = "class Sample { }\n";
        const string documentation = "# Notes\n";
        await CreateProjectAsync(original);
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), documentation);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceValidationResult result = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Candidate,
            [new(new("README.md"), new(Hash(documentation)), new("# Updated\n"))]));

        Assert.Equal(CodeIntelligenceValidationDisposition.NotApplicable, result.Disposition);
        Assert.Equal("document_not_in_workspace", Assert.Single(result.Issues).Code.Value);
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

    [Fact]
    public async Task Actual_harness_workspace_meets_the_bounded_foreground_session_budget()
    {
        string repository = FindRepositoryRoot();
        const string relativePath =
            "src/Harness.BusinessLogic/Documents/WorkbenchDocumentTypes.cs";
        string source = await File.ReadAllTextAsync(
            Path.Combine(repository, relativePath),
            Utf8WithoutBom);
        long beforeBytes = GC.GetTotalMemory(forceFullCollection: true);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("harness-performance-context");
        Stopwatch cold = Stopwatch.StartNew();
        CodeIntelligenceSessionResult session = await engine.OpenAsync(new(
            contextId,
            new(repository),
            new("Harness.slnx"),
            CodeIntelligenceSourceKind.OriginalWorkspace));
        cold.Stop();
        Assert.NotEqual(CodeIntelligenceResultState.Failed, session.State);

        CodeIntelligenceDocumentSnapshot snapshot = new(
            contextId,
            session.SessionId!,
            new(relativePath),
            new(Hash(source)),
            new(1),
            new(source));
        _ = await engine.GetDiagnosticsAsync(snapshot);
        Stopwatch warm = Stopwatch.StartNew();
        CodeIntelligenceDiagnosticResult updated = await engine.GetDiagnosticsAsync(
            snapshot with
            {
                BufferVersion = new(2),
                Text = new(source + " "),
            });
        warm.Stop();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Stopwatch cancelled = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GetDiagnosticsAsync(
            snapshot with { BufferVersion = new(3) },
            cancellation.Token).AsTask());
        cancelled.Stop();
        long retainedBytes = Math.Max(0, GC.GetTotalMemory(forceFullCollection: true) - beforeBytes);

        output.WriteLine($"cold_load_ms={cold.Elapsed.TotalMilliseconds:F0}");
        output.WriteLine($"warm_update_ms={warm.Elapsed.TotalMilliseconds:F0}");
        output.WriteLine($"retained_memory_mib={retainedBytes / 1024d / 1024d:F1}");
        output.WriteLine($"cancellation_ms={cancelled.Elapsed.TotalMilliseconds:F1}");
        Assert.NotEqual(CodeIntelligenceResultState.Failed, updated.State);
        Assert.True(cold.Elapsed < TimeSpan.FromSeconds(60), $"Cold load took {cold.Elapsed}.");
        Assert.True(warm.Elapsed < TimeSpan.FromSeconds(15), $"Warm update took {warm.Elapsed}.");
        Assert.True(retainedBytes < 1024L * 1024 * 1024,
            $"Foreground session retained {retainedBytes / 1024d / 1024d:F1} MiB.");
        Assert.True(cancelled.Elapsed < TimeSpan.FromSeconds(1),
            $"Cancellation took {cancelled.Elapsed}.");
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

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Harness.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new InvalidOperationException("Harness.slnx was not found above the test output.");
    }

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
