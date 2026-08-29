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
    private async ValueTask CreateProjectAsync(string source)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"),
            RepositorySdkPolicy.GlobalJson);
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

    private static void EmitMetadataDependency(
        string path,
        string source,
        bool metadataOnly = false)
    {
        MetadataReference[] references = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "External",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));
        using FileStream output = File.Create(path);
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(
            output,
            options: new Microsoft.CodeAnalysis.Emit.EmitOptions(
                metadataOnly: metadataOnly,
                includePrivateMembers: !metadataOnly));
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
    }

    private async ValueTask CreateLinkedSolutionAsync(string shared)
    {
        Directory.CreateDirectory(Path.Combine(root, "First"));
        Directory.CreateDirectory(Path.Combine(root, "Second"));
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"),
            RepositorySdkPolicy.GlobalJson);
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="../Shared.cs" Link="Shared.cs" /></ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "First", "First.csproj"), project);
        await File.WriteAllTextAsync(Path.Combine(root, "Second", "Second.csproj"), project);
        await File.WriteAllTextAsync(Path.Combine(root, "Shared.cs"), shared, Utf8WithoutBom);
        await File.WriteAllTextAsync(
            Path.Combine(root, "First", "Use.cs"),
            "class FirstUse { Widget value = new(); }\n",
            Utf8WithoutBom);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Second", "Use.cs"),
            "class SecondUse { Widget value = new(); }\n",
            Utf8WithoutBom);
        await File.WriteAllTextAsync(Path.Combine(root, "Linked.slnx"), """
            <Solution>
              <Project Path="First/First.csproj" />
              <Project Path="Second/Second.csproj" />
            </Solution>
            """);
    }

    private async ValueTask CreateDependentSolutionAsync(string contract)
    {
        Directory.CreateDirectory(Path.Combine(root, "Contracts"));
        Directory.CreateDirectory(Path.Combine(root, "Consumer"));
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"),
            RepositorySdkPolicy.GlobalJson);
        await File.WriteAllTextAsync(Path.Combine(root, "Contracts", "Contracts.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Contracts", "Contract.cs"),
            contract,
            Utf8WithoutBom);
        await File.WriteAllTextAsync(Path.Combine(root, "Consumer", "Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Contracts/Contracts.csproj" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Consumer", "Use.cs"),
            "using Contracts; class Use { int Read(Contract value) => value.Value; }\n",
            Utf8WithoutBom);
        await File.WriteAllTextAsync(Path.Combine(root, "Dependent.slnx"), """
            <Solution>
              <Project Path="Contracts/Contracts.csproj" />
              <Project Path="Consumer/Consumer.csproj" />
            </Solution>
            """);
    }

    private RoslynCodeIntelligenceEngine CreateEngine() => new(
        new MSBuildRuntime(new(new DotNetProcess())));

    private CodeIntelligenceOpenRequest OpenRequest(CodeIntelligenceContextId contextId) => new(
        contextId,
        new(root),
        new("Sample.csproj"),
        CodeIntelligenceSourceKind.ApprovedGoalWorktree);

    private static CodeIntelligenceInteractiveSnapshot InteractiveSnapshot(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceSessionId sessionId,
        string source,
        int offset)
        => InteractiveSnapshot(contextId, sessionId, source, source, offset);

    private static CodeIntelligenceInteractiveSnapshot InteractiveSnapshot(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceSessionId sessionId,
        string baselineSource,
        string source,
        int offset,
        string path = "Sample.cs")
    {
        string before = source[..offset];
        int line = before.Count(character => character == '\n');
        int lastBreak = before.LastIndexOf('\n');
        int character = lastBreak < 0 ? before.Length : before.Length - lastBreak - 1;
        return new(
            contextId,
            sessionId,
            new(path),
            new(Hash(baselineSource)),
            new(1),
            new(source),
            new(line, character));
    }

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

#pragma warning disable RS2008, RS1036, RS1038, RS1041 // Test-only in-process analyzer fixtures.
[Microsoft.CodeAnalysis.Generator]
public sealed class HarnessVirtualDocumentTestGenerator : Microsoft.CodeAnalysis.IIncrementalGenerator
{
    public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext context) =>
        context.RegisterPostInitializationOutput(output => output.AddSource(
            "GeneratedWidget.g.cs",
            "internal static class GeneratedWidget { internal const int Number = 42; }\n"));
}

[Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class HarnessFailingDiagnosticAnalyzer
    : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer
{
    private static readonly Microsoft.CodeAnalysis.DiagnosticDescriptor Rule = new(
        "HARNESSFAIL001",
        "Failing analyzer fixture",
        "Failing analyzer fixture",
        "Tests",
        Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.DiagnosticDescriptor>
        SupportedDiagnostics => [Rule];

    public override void Initialize(
        Microsoft.CodeAnalysis.Diagnostics.AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            Microsoft.CodeAnalysis.Diagnostics.GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(analysis =>
        {
            if (analysis.Tree.GetText(analysis.CancellationToken).ToString()
                .Contains("FAIL_ANALYZER", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Test-only analyzer failure.");
            }
        });
    }
}

[Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class HarnessSlowDiagnosticAnalyzer
    : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer
{
    private static readonly Microsoft.CodeAnalysis.DiagnosticDescriptor Rule = new(
        "HARNESSSLOW001",
        "Slow analyzer fixture",
        "Slow analyzer fixture",
        "Tests",
        Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.DiagnosticDescriptor>
        SupportedDiagnostics => [Rule];

    public override void Initialize(
        Microsoft.CodeAnalysis.Diagnostics.AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            Microsoft.CodeAnalysis.Diagnostics.GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(analysis =>
        {
            if (!analysis.Tree.GetText(analysis.CancellationToken).ToString()
                .Contains("SLOW_ANALYZER", StringComparison.Ordinal))
            {
                return;
            }

            while (true)
            {
                analysis.CancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(10);
            }
        });
    }
}
#pragma warning restore RS2008, RS1036, RS1038, RS1041
