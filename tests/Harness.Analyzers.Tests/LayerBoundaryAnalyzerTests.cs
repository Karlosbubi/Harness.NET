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

    [Theory]
    [InlineData("public interface IService { IDataStore Read(); }")]
    [InlineData("public interface IService { IDataStore Value { get; } }")]
    [InlineData("public interface IService { void Write(IDataStore value); }")]
    [InlineData("public interface IService { System.Collections.Generic.List<IDataStore> Read(); }")]
    [InlineData("public interface IService { System.Threading.Tasks.Task<System.Collections.Generic.List<IDataStore>> Read(); }")]
    [InlineData("public interface IService { (IDataStore Store, string Name) Read(); }")]
    [InlineData("public record Result(IDataStore Store);")]
    [InlineData("public interface IService : IDataStore { }")]
    public async Task Business_logic_public_contract_cannot_reach_data_access_type(string declaration)
    {
        MetadataReference dataAccess = CreateReference(
            "Harness.DataAccess",
            "namespace Harness.DataAccess; public interface IDataStore { }");
        string source = $$"""
            using Harness.DataAccess;
            namespace Harness.BusinessLogic;
            {{declaration}}
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.BusinessLogic", source, dataAccess);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.DataAccessLeakId &&
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Business_logic_private_implementation_may_use_data_access_type()
    {
        MetadataReference dataAccess = CreateReference(
            "Harness.DataAccess",
            "namespace Harness.DataAccess; public interface IDataStore { }");
        const string source = """
            using Harness.DataAccess;
            namespace Harness.BusinessLogic;
            public interface IService { string Read(); }
            internal sealed class Service(IDataStore store) : IService
            {
                public string Read() => store.ToString();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.BusinessLogic", source, dataAccess);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.DataAccessLeakId);
    }

    [Fact]
    public async Task Business_logic_contract_may_expose_bcl_and_microsoft_extensions_types()
    {
        MetadataReference logging = CreateReference(
            "Microsoft.Extensions.Logging.Abstractions",
            "namespace Microsoft.Extensions.Logging; public interface ILogger { }");
        const string source = """
            namespace Harness.BusinessLogic;
            public interface IService
            {
                System.Threading.Tasks.Task<string> ReadAsync(
                    Microsoft.Extensions.Logging.ILogger logger);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.BusinessLogic", source, logging);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.DataAccessLeakId);
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

    [Fact]
    public async Task Public_semantic_enum_is_allowed()
    {
        const string source = "namespace Harness.BusinessLogic; public enum GoalState { Draft, Approved }";

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.BusinessLogic",
            source);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.InvalidBoundaryTypeId);
    }

    [Fact]
    public async Task Ui_toolkit_cannot_use_business_logic_symbols()
    {
        MetadataReference businessLogic = CreateReference(
            "Harness.BusinessLogic",
            "namespace Harness.BusinessLogic; public interface IGoalService { }");
        const string source = """
            using Harness.BusinessLogic;
            namespace Harness.UI.Avalonia;
            public sealed class GoalControl { private IGoalService? service; }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.UI.Avalonia",
            source,
            businessLogic);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == LayerBoundaryAnalyzer.InvalidLayerUsageId);
    }

    [Fact]
    public async Task Ui_toolkit_may_expose_public_framework_classes()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            "Harness.UI.Avalonia",
            "namespace Harness.UI.Avalonia; public sealed class ThemeController { }");

        Assert.DoesNotContain(diagnostics, diagnostic =>
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
