using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.CodeIntelligence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace Harness.DataAccess.Tests.CodeIntelligence;

public sealed partial class RoslynCodeIntelligenceEngineTests
{
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
    public async Task Failing_analyzer_degrades_diagnostics_without_hiding_compiler_errors()
    {
        const string source = "// FAIL_ANALYZER\nclass Sample { void Run() { int value = ; } }\n";
        await CreateProjectAsync(source);
        string analyzer = typeof(HarnessFailingDiagnosticAnalyzer).Assembly.Location
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Analyzer Include="{analyzer}" /></ItemGroup>
            </Project>
            """);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("failing-analyzer-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDiagnosticResult result = await engine.GetDiagnosticsAsync(new(
            contextId,
            session.SessionId!,
            new("Sample.cs"),
            new(Hash(source)),
            new(1),
            new(source)));

        Assert.Equal(CodeIntelligenceResultState.Degraded, result.State);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id.Value.StartsWith("CS", StringComparison.Ordinal) &&
            diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Id.Value is "AD0001" or "AD0002");
        CodeIntelligenceIssue issue = Assert.Single(result.Issues,
            item => item.Code.Value == "analyzer_failed");
        Assert.Equal(
            "One or more project analyzers failed. Compiler diagnostics remain available.",
            issue.Message.Value);
    }

    [Fact]
    public async Task In_flight_analyzer_diagnostics_honor_cancellation_promptly()
    {
        const string source = "// SLOW_ANALYZER\nclass Sample { }\n";
        await CreateProjectAsync(source);
        string analyzer = typeof(HarnessSlowDiagnosticAnalyzer).Assembly.Location
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Analyzer Include="{analyzer}" /></ItemGroup>
            </Project>
            """);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("slow-analyzer-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
        Stopwatch elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.GetDiagnosticsAsync(new(
                contextId,
                session.SessionId!,
                new("Sample.cs"),
                new(Hash(source)),
                new(1),
                new(source)),
                cancellation.Token).AsTask());

        elapsed.Stop();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2),
            $"In-flight analyzer cancellation took {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task Repeated_foreground_context_switches_keep_only_the_latest_session_usable()
    {
        const string source = "class Sample { int Value { get; } }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId? previousContext = null;
        CodeIntelligenceSessionId? previousSession = null;

        for (int index = 0; index < 8; index++)
        {
            CodeIntelligenceContextId context = new($"repeated-context-{index}");
            CodeIntelligenceSessionResult opened = await engine.OpenAsync(OpenRequest(context));
            Assert.NotEqual(CodeIntelligenceResultState.Failed, opened.State);

            if (previousContext is not null && previousSession is not null)
            {
                CodeIntelligenceDiagnosticResult stale = await engine.GetDiagnosticsAsync(new(
                    previousContext,
                    previousSession,
                    new("Sample.cs"),
                    new(Hash(source)),
                    new(index),
                    new(source)));
                Assert.Equal(CodeIntelligenceResultState.Stale, stale.State);
                Assert.Equal("session_unavailable", Assert.Single(stale.Issues).Code.Value);
            }

            CodeIntelligenceDiagnosticResult current = await engine.GetDiagnosticsAsync(new(
                context,
                opened.SessionId!,
                new("Sample.cs"),
                new(Hash(source)),
                new(index + 1),
                new(source)));
            Assert.NotEqual(CodeIntelligenceResultState.Failed, current.State);
            previousContext = context;
            previousSession = opened.SessionId;
        }
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

        string completionPrefix = source +
            "\ninternal sealed class CompletionProbe { void Run() { " +
            "var path = new WorkbenchDocumentPath(\"x\"); path.Va";
        string completionSource = completionPrefix + " } }\n";
        CodeIntelligenceInteractiveSnapshot interactive = InteractiveSnapshot(
            contextId,
            session.SessionId!,
            source,
            completionSource,
            completionPrefix.Length,
            relativePath);
        CodeIntelligenceCompletionResult warmedCompletion = await engine.GetCompletionsAsync(new(
            interactive,
            CodeIntelligenceCompletionTriggerKind.Invoke,
            TriggerCharacter: null));
        List<double> completionMilliseconds = [];
        for (int index = 0; index < 20; index++)
        {
            Stopwatch completion = Stopwatch.StartNew();
            _ = await engine.GetCompletionsAsync(new(
                interactive,
                CodeIntelligenceCompletionTriggerKind.Invoke,
                TriggerCharacter: null));
            completion.Stop();
            completionMilliseconds.Add(completion.Elapsed.TotalMilliseconds);
        }

        completionMilliseconds.Sort();
        double completionP95 = completionMilliseconds[18];
        string navigationSource = source +
            "\ninternal sealed class NavigationProbe { WorkbenchDocumentPath? Value { get; } }\n";
        int symbolOffset = navigationSource.LastIndexOf(
            "WorkbenchDocumentPath", StringComparison.Ordinal) + 5;
        Stopwatch navigation = Stopwatch.StartNew();
        CodeIntelligenceNavigationResult definition = await engine.FindDefinitionAsync(
            InteractiveSnapshot(
                contextId,
                session.SessionId!,
                source,
                navigationSource,
                symbolOffset,
                relativePath));
        navigation.Stop();

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
        output.WriteLine($"completion_p95_ms={completionP95:F1}");
        output.WriteLine($"navigation_ms={navigation.Elapsed.TotalMilliseconds:F1}");
        output.WriteLine($"completion_state={warmedCompletion.State}");
        output.WriteLine($"completion_items={warmedCompletion.Items.Count}");
        output.WriteLine("completion_issues=" + string.Join(" | ",
            warmedCompletion.Issues.Select(issue =>
                $"{issue.Code.Value}:{issue.Message.Value}")));
        Assert.NotEqual(CodeIntelligenceResultState.Failed, updated.State);
        Assert.True(cold.Elapsed < TimeSpan.FromSeconds(60), $"Cold load took {cold.Elapsed}.");
        Assert.True(warm.Elapsed < TimeSpan.FromSeconds(15), $"Warm update took {warm.Elapsed}.");
        Assert.True(retainedBytes < 1024L * 1024 * 1024,
            $"Foreground session retained {retainedBytes / 1024d / 1024d:F1} MiB.");
        Assert.True(cancelled.Elapsed < TimeSpan.FromSeconds(1),
            $"Cancellation took {cancelled.Elapsed}.");
        Assert.True(completionP95 < 200,
            $"Warm completion p95 was {completionP95:F1} ms (target < 200 ms).");
        Assert.Contains(warmedCompletion.Items, item =>
            item.DisplayText.Value == "Value");
        Assert.NotEmpty(definition.Destinations);
        Assert.True(navigation.Elapsed < TimeSpan.FromSeconds(2),
            $"Warm definition navigation took {navigation.Elapsed}.");
    }

}
