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
    public async Task Rename_preview_reports_semantic_name_conflicts_without_a_fingerprint()
    {
        const string source = "class Existing { } class Widget { Widget value = new(); }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-conflict-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("Widget", StringComparison.Ordinal) + 2;

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, source, offset),
            new("Existing")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Conflicted, result.Disposition);
        Assert.Null(result.Fingerprint);
        Assert.Contains(result.Conflicts, conflict =>
            conflict.Kind is CodeIntelligenceRenameConflictKind.Semantic);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Rename_preview_rejects_invalid_identifiers_before_resolving_a_symbol()
    {
        const string source = "class Widget { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-invalid-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, source, 7),
            new("class")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Rejected, result.Disposition);
        Assert.Equal("invalid_identifier", Assert.Single(result.Issues).Code.Value);
    }

    [Fact]
    public async Task Rename_preview_targets_one_overload_and_its_bound_calls()
    {
        const string source = """
            class Sample
            {
                void Run(int value) { }
                void Run(string value) { }
                void Test() { Run(1); Run("x"); }
            }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-overload-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("Run(int", StringComparison.Ordinal) + 2;

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, source, offset),
            new("Execute")));

        CodeIntelligenceRenameEdit edit = Assert.Single(result.Edits);
        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.Contains("Execute(int", edit.Text.Value, StringComparison.Ordinal);
        Assert.Contains("Execute(1)", edit.Text.Value, StringComparison.Ordinal);
        Assert.Contains("Run(string", edit.Text.Value, StringComparison.Ordinal);
        Assert.Contains("Run(\"x\")", edit.Text.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_preview_reports_metadata_symbols_as_uneditable()
    {
        const string source = "class Sample { string Value = string.Empty; }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-metadata-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("string Value", StringComparison.Ordinal) + 2;

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, source, offset),
            new("Text")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Conflicted, result.Disposition);
        Assert.Contains(result.Conflicts, conflict =>
            conflict.Kind is CodeIntelligenceRenameConflictKind.Metadata);
    }

    [Fact]
    public async Task Metadata_definition_opens_version_bound_read_only_decompiled_source()
    {
        const string source = "class Sample { string Value = string.Empty; }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("metadata-virtual-document-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("Empty", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceNavigationResult navigation = await engine.FindDefinitionAsync(snapshot);
        CodeIntelligenceSymbolDestination destination = Assert.Single(navigation.Destinations);
        Assert.Equal(CodeIntelligenceDestinationKind.Metadata, destination.Kind);
        Assert.NotNull(destination.VirtualDocumentId);

        CodeIntelligenceVirtualDocumentResult document = await engine.GetVirtualDocumentAsync(
            new(snapshot, destination.VirtualDocumentId!));

        Assert.Equal(CodeIntelligenceResultState.Ready, document.State);
        Assert.Equal(CodeIntelligenceVirtualDocumentKind.DecompiledSource, document.Kind);
        Assert.True(document.IsReadOnly);
        Assert.Contains("Decompiled locally by Harness.NET", document.Text!.Value,
            StringComparison.Ordinal);
        Assert.Contains("Empty", document.Text.Value, StringComparison.Ordinal);
        Assert.Equal("net10.0", document.Origin!.TargetFramework.Value);
        Assert.Equal(64, document.Origin.Compilation.Value.Length);
        Assert.False(File.Exists(Path.Combine(root, "String.cs")));

        CodeIntelligenceVirtualDocumentResult stale = await engine.GetVirtualDocumentAsync(new(
            snapshot with { BufferVersion = new(2) }, destination.VirtualDocumentId!));
        Assert.Equal(CodeIntelligenceResultState.Stale, stale.State);
        Assert.Equal("virtual_document_stale", Assert.Single(stale.Issues).Code.Value);
    }

    [Fact]
    public async Task Metadata_method_navigation_reconstructs_the_local_implementation_body()
    {
        const string source =
            "class Sample { int Run() => new External.Calculator().Double(21); }\n";
        await CreateProjectAsync(source);
        string dependencyPath = Path.Combine(root, "External.dll");
        EmitMetadataDependency(dependencyPath, """
            namespace External;
            public sealed class Calculator
            {
                public int Double(int value) => value * 2;
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Reference Include="External" HintPath="External.dll" /></ItemGroup>
            </Project>
            """);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("metadata-method-body-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("Double", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceNavigationResult navigation = await engine.FindDefinitionAsync(snapshot);
        CodeIntelligenceSymbolDestination destination = Assert.Single(navigation.Destinations);
        CodeIntelligenceVirtualDocumentResult document = await engine.GetVirtualDocumentAsync(
            new(snapshot, destination.VirtualDocumentId!));

        Assert.Equal(CodeIntelligenceResultState.Ready, document.State);
        Assert.Equal(CodeIntelligenceVirtualDocumentKind.DecompiledSource, document.Kind);
        Assert.Contains("int Double(int value)", document.Text!.Value, StringComparison.Ordinal);
        Assert.Contains("return value * 2;", document.Text.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(root, document.Text.Value, StringComparison.Ordinal);
        Assert.Empty(document.Issues);
    }

    [Fact]
    public async Task Reference_only_metadata_falls_back_to_an_explicit_signature_view()
    {
        const string source =
            "class Sample { int Run() => new External.Calculator().Double(21); }\n";
        await CreateProjectAsync(source);
        string dependencyPath = Path.Combine(root, "External.dll");
        EmitMetadataDependency(dependencyPath, """
            namespace External;
            public sealed class Calculator
            {
                public int Double(int value) => value * 2;
            }
            """, metadataOnly: true);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Reference Include="External" HintPath="External.dll" /></ItemGroup>
            </Project>
            """);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("metadata-signature-fallback-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("Double", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceNavigationResult navigation = await engine.FindDefinitionAsync(snapshot);
        CodeIntelligenceVirtualDocumentResult document = await engine.GetVirtualDocumentAsync(
            new(snapshot, Assert.Single(navigation.Destinations).VirtualDocumentId!));

        Assert.Equal(CodeIntelligenceVirtualDocumentKind.MetadataSignature, document.Kind);
        Assert.Contains("Method bodies are not decompiled", document.Text!.Value,
            StringComparison.Ordinal);
        Assert.Equal("decompilation_unavailable", Assert.Single(document.Issues).Code.Value);
    }

    [Fact]
    public async Task Generated_definition_opens_the_exact_read_only_generator_output()
    {
        const string source = "class Sample { int Value = GeneratedWidget.Number; }\n";
        await CreateProjectAsync(source);
        string analyzer = typeof(HarnessVirtualDocumentTestGenerator).Assembly.Location
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Analyzer Include="{analyzer}" /></ItemGroup>
            </Project>
            """);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("generated-virtual-document-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("GeneratedWidget", StringComparison.Ordinal) + 4;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceNavigationResult navigation = await engine.FindDefinitionAsync(snapshot);
        CodeIntelligenceSymbolDestination destination = Assert.Single(navigation.Destinations);
        Assert.Equal(CodeIntelligenceDestinationKind.Generated, destination.Kind);
        Assert.NotNull(destination.VirtualDocumentId);

        CodeIntelligenceVirtualDocumentResult document = await engine.GetVirtualDocumentAsync(
            new(snapshot, destination.VirtualDocumentId!));

        Assert.Equal(CodeIntelligenceVirtualDocumentKind.GeneratedSource, document.Kind);
        Assert.Contains("GeneratedWidget", document.Text!.Value, StringComparison.Ordinal);
        Assert.Contains("Number = 42", document.Text.Value, StringComparison.Ordinal);
        Assert.True(document.IsReadOnly);
        Assert.NotNull(document.SelectionRange);
        Assert.False(File.Exists(Path.Combine(root, "GeneratedWidget.g.cs")));
    }

    [Fact]
    public async Task Exact_buffer_inspections_return_syntax_symbol_and_method_il()
    {
        const string source = """
            class Sample
            {
                string Add(string left, string right) => string.Concat(left, right);

                int Add(int left, int right)
                {
                    return left + right;
                }
            }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("inspection-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("left +", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceInspectionResult syntax = await engine.InspectAsync(new(
            snapshot, CodeIntelligenceInspectionKind.SyntaxTree));
        CodeIntelligenceInspectionResult symbol = await engine.InspectAsync(new(
            snapshot, CodeIntelligenceInspectionKind.Symbol));
        CodeIntelligenceInspectionResult il = await engine.InspectAsync(new(
            snapshot, CodeIntelligenceInspectionKind.IntermediateLanguage));

        Assert.Equal(CodeIntelligenceResultState.Ready, syntax.State);
        Assert.Contains("MethodDeclaration", syntax.Text!.Value, StringComparison.Ordinal);
        Assert.Contains("ReturnStatement", syntax.Text.Value, StringComparison.Ordinal);
        Assert.Equal(CodeIntelligenceResultState.Ready, symbol.State);
        Assert.Contains("Kind: Parameter", symbol.Text!.Value, StringComparison.Ordinal);
        Assert.Contains("Type: int", symbol.Text.Value, StringComparison.Ordinal);
        Assert.Equal(CodeIntelligenceResultState.Ready, il.State);
        Assert.Contains("Selected symbol:", il.Text!.Value, StringComparison.Ordinal);
        Assert.Contains("Candidate bodies: 1", il.Text.Value, StringComparison.Ordinal);
        Assert.Contains(": add", il.Text.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(": ret", il.Text.Value, StringComparison.OrdinalIgnoreCase);
        Assert.All(new[] { syntax, symbol, il }, result =>
        {
            Assert.True(result.IsReadOnly);
            Assert.Equal(64, result.Origin!.Compilation.Value.Length);
            Assert.Equal("net10.0", result.Origin.TargetFramework.Value);
        });

    }

    [Fact]
    public async Task Generated_source_inspection_lists_exact_generator_output_without_writing()
    {
        const string source = "class Sample { int Value = GeneratedWidget.Number; }\n";
        await CreateProjectAsync(source);
        string analyzer = typeof(HarnessVirtualDocumentTestGenerator).Assembly.Location
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Analyzer Include="{analyzer}" /></ItemGroup>
            </Project>
            """);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("generated-inspection-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        CodeIntelligenceInteractiveSnapshot snapshot = InteractiveSnapshot(
            contextId, session.SessionId!, source,
            source.IndexOf("GeneratedWidget", StringComparison.Ordinal) + 2);

        CodeIntelligenceInspectionResult result = await engine.InspectAsync(new(
            snapshot, CodeIntelligenceInspectionKind.GeneratedSource));

        Assert.Equal(CodeIntelligenceResultState.Ready, result.State);
        Assert.Contains("GeneratedWidget.g.cs", result.Text!.Value, StringComparison.Ordinal);
        Assert.Contains("Number = 42", result.Text.Value, StringComparison.Ordinal);
        Assert.False(result.IsTruncated);
        Assert.False(File.Exists(Path.Combine(root, "GeneratedWidget.g.cs")));
    }

    [Fact]
    public async Task Rename_preview_keeps_a_large_bounded_file_set_complete()
    {
        const string declaration = "public class Widget { }\n";
        await CreateProjectAsync(declaration);
        for (int index = 0; index < 24; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, $"Use{index:D2}.cs"),
                $"class Use{index:D2} {{ Widget value = new(); }}\n",
                Utf8WithoutBom);
        }

        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-large-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, declaration, 15),
            new("Gadget")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.Equal(25, result.Edits.Count);
        Assert.Equal(25, result.Edits.Select(edit => edit.Path.Value).Distinct().Count());
        Assert.NotNull(result.Fingerprint);
    }

    [Fact]
    public async Task Rename_preview_rejects_an_unwritable_affected_source_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string declaration = "public class Widget { }\n";
        const string use = "class Use { Widget value = new(); }\n";
        await CreateProjectAsync(declaration);
        string usePath = Path.Combine(root, "Use.cs");
        await File.WriteAllTextAsync(usePath, use, Utf8WithoutBom);
        File.SetUnixFileMode(usePath, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-unwritable-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, declaration, 15),
            new("Gadget")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Conflicted, result.Disposition);
        Assert.Contains(result.Conflicts, conflict =>
            conflict.Kind is CodeIntelligenceRenameConflictKind.Uneditable &&
            conflict.Path?.Value == "Use.cs");
    }

    [Fact]
    public async Task Rename_preview_coalesces_linked_documents_by_physical_path()
    {
        const string shared = "public class Widget { }\n";
        await CreateLinkedSolutionAsync(shared);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-linked-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(new(
            contextId,
            new(root),
            new("Linked.slnx"),
            CodeIntelligenceSourceKind.ApprovedGoalWorktree));

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(
                contextId,
                session.SessionId!,
                shared,
                shared,
                15,
                "Shared.cs"),
            new("Gadget")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.Equal(3, result.Edits.Count);
        Assert.Single(result.Edits, edit => edit.Path.Value == "Shared.cs");
        Assert.DoesNotContain(result.Conflicts, conflict =>
            conflict.Kind is CodeIntelligenceRenameConflictKind.InconsistentLinkedFile);
    }

}
