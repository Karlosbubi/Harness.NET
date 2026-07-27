using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Harness.Analyzers.Tests;

public sealed class LayerBoundaryAnalyzerTests
{
    private static readonly MetadataReference CoreReference =
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    [Fact]
    public async Task Presentation_cannot_use_data_access_symbols()
    {
        MetadataReference dataAccess = CreateReference(
            "Harness.DataAccess",
            "namespace Harness.DataAccess; public interface IDataStore { }");

        const string source = """
            using Harness.DataAccess;
            namespace Harness.Presentation.Terminal;
            internal sealed class View(IDataStore store) { }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.Presentation.Terminal",
            source,
            dataAccess);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.InvalidLayerUsageId);
    }

    [Fact]
    public async Task Business_logic_can_use_data_access_interfaces()
    {
        MetadataReference dataAccess = CreateReference(
            "Harness.DataAccess",
            "namespace Harness.DataAccess; public interface IDataStore { }");

        const string source = """
            using Harness.DataAccess;
            namespace Harness.BusinessLogic;
            public interface IService : IDataStore { }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.BusinessLogic",
            source,
            dataAccess);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.InvalidLayerUsageId);
    }

    [Fact]
    public async Task Public_layer_class_is_rejected()
    {
        const string source = "namespace Harness.BusinessLogic; public sealed class Service { }";

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.BusinessLogic",
            source);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.InvalidBoundaryTypeId);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [CoreReference, .. additionalReferences],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return await compilation
            .WithAnalyzers([new LayerBoundaryAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference CreateReference(string assemblyName, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [CoreReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
