using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.CodeIntelligence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace Harness.DataAccess.Tests.CodeIntelligence;

[Collection("Roslyn workspace compatibility")]
public sealed partial class RoslynCodeIntelligenceEngineTests(ITestOutputHelper output) : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-roslyn-engine-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Composes_the_pinned_Roslyn_code_fix_catalog()
    {
        IReadOnlyList<string> providers =
            RoslynCodeIntelligenceEngine.ClosedCodeFixProviderNames();

        Assert.Contains("CSharpImplementInterfaceCodeFixProvider", providers);
        Assert.Contains("CSharpImplementAbstractClassCodeFixProvider", providers);
        Assert.Contains("CSharpAddExplicitCastCodeFixProvider", providers);
        Assert.Contains("AssignOutParametersAboveReturnCodeFixProvider", providers);
        Assert.Contains("AssignOutParametersAtStartCodeFixProvider", providers);
        Assert.Contains("CSharpUseObjectInitializerCodeFixProvider", providers);
        Assert.Contains("CSharpGenerateDefaultConstructorsCodeFixProvider", providers);
        Assert.Contains("CSharpGenerateVariableCodeFixProvider", providers);
        Assert.Contains("CSharpAddParameterCodeFixProvider", providers);
        Assert.Contains("CSharpFixReturnTypeCodeFixProvider", providers);
        Assert.Contains("CSharpMakeMemberStaticCodeFixProvider", providers);
        Assert.Contains("CSharpMakeTypeAbstractCodeFixProvider", providers);
        Assert.Contains("CSharpMakeTypePartialCodeFixProvider", providers);
        Assert.Contains("CSharpRemoveUnnecessaryCastCodeFixProvider", providers);
        Assert.Contains("SimplifyTypeNamesCodeFixProvider", providers);
        Assert.Contains("CSharpUseNullPropagationCodeFixProvider", providers);
        Assert.Contains("CSharpUseCompoundAssignmentCodeFixProvider", providers);
        Assert.Contains("CSharpAddBracesCodeFixProvider", providers);
        Assert.Contains("CSharpInlineDeclarationCodeFixProvider", providers);
        Assert.Contains("CSharpUseCollectionInitializerCodeFixProvider", providers);

        IReadOnlyList<string> refactorings =
            RoslynCodeIntelligenceEngine.ClosedCodeRefactoringProviderNames();
        Assert.Contains("CSharpConvertAutoPropertyToFullPropertyCodeRefactoringProvider",
            refactorings);
        Assert.Contains("CSharpConvertIfToSwitchCodeRefactoringProvider", refactorings);
        Assert.Contains("CSharpInlineTemporaryCodeRefactoringProvider", refactorings);
        Assert.Contains("UseExplicitTypeCodeRefactoringProvider", refactorings);
        Assert.Contains("UseImplicitTypeCodeRefactoringProvider", refactorings);
        Assert.Contains("ExtractMethodCodeRefactoringProvider", refactorings);
        Assert.Contains("IntroduceVariableCodeRefactoringProvider", refactorings);
    }

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
    public async Task Candidate_validation_includes_transitive_dependent_projects()
    {
        const string original = "namespace Contracts; public sealed class Contract { public int Value { get; } }\n";
        const string candidate = "namespace Contracts; public sealed class Contract { }\n";
        await CreateDependentSolutionAsync(original);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(new(
            contextId,
            new(root),
            new("Dependent.slnx"),
            CodeIntelligenceSourceKind.ApprovedGoalWorktree));

        CodeIntelligenceValidationResult result = await engine.ValidateAsync(new(
            contextId,
            session.SessionId!,
            CodeIntelligenceValidationPhase.Candidate,
            [new(new("Contracts/Contract.cs"), new(Hash(original)), new(candidate))]));

        Assert.Equal(CodeIntelligenceValidationDisposition.Rejected, result.Disposition);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
            diagnostic.Diagnostic.Project?.Value == "Consumer" &&
            diagnostic.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error);
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
    public async Task Completion_is_local_committable_and_bound_to_the_exact_buffer()
    {
        const string source = """
            class Widget { public int Value { get; } }
            class Use { void Run() { var widget = new Widget(); widget.Va } }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        CodeIntelligenceInteractiveSnapshot snapshot = InteractiveSnapshot(
            contextId, session.SessionId!, source, source.IndexOf("Va }", StringComparison.Ordinal) + 2);

        CodeIntelligenceCompletionResult completions = await engine.GetCompletionsAsync(new(
            snapshot,
            CodeIntelligenceCompletionTriggerKind.Invoke,
            TriggerCharacter: null));
        CodeIntelligenceCompletionItem value = Assert.Single(
            completions.Items,
            item => item.DisplayText.Value == "Value");
        CodeIntelligenceCompletionCommitResult committed = await engine.CommitCompletionAsync(new(
            snapshot,
            completions.ListId!,
            value.Id,
            CommitCharacter: null));

        Assert.Equal(CodeIntelligenceResultState.Ready, completions.State);
        Assert.Contains(committed.Changes, change => change.Text.Value.Contains("Value", StringComparison.Ordinal));
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Quick_info_signature_definition_and_references_resolve_source_symbols()
    {
        const string source = """
            class Widget
            {
                public int Value { get; }
                /// <summary>Runs one operation.</summary>
                /// <param name="text">The input text.</param>
                /// <param name="count">The repeat count.</param>
                public void Run(string text, int count) { }
            }
            class Use
            {
                void Test()
                {
                    var widget = new Widget();
                    var value = widget.Value;
                    widget.Run("x", 1);
                }
            }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("context-1");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int valueUse = source.LastIndexOf("Value", StringComparison.Ordinal);
        CodeIntelligenceInteractiveSnapshot valueSnapshot = InteractiveSnapshot(
            contextId, session.SessionId!, source, valueUse + 2);
        int callStart = source.IndexOf("Run(\"x\"", StringComparison.Ordinal);
        CodeIntelligenceInteractiveSnapshot callSnapshot = InteractiveSnapshot(
            contextId, session.SessionId!, source, callStart + 4);

        CodeIntelligenceQuickInfoResult quickInfo = await engine.GetQuickInfoAsync(valueSnapshot);
        CodeIntelligenceNavigationResult definition = await engine.FindDefinitionAsync(valueSnapshot);
        CodeIntelligenceNavigationResult references = await engine.FindReferencesAsync(valueSnapshot);
        CodeIntelligenceSignatureHelpResult signatures =
            await engine.GetSignatureHelpAsync(callSnapshot);
        int comma = source.IndexOf(", 1", callStart, StringComparison.Ordinal);
        CodeIntelligenceSignatureHelpResult secondParameter =
            await engine.GetSignatureHelpAsync(InteractiveSnapshot(
                contextId, session.SessionId!, source, comma + 1));
        int stringType = source.IndexOf("string text", StringComparison.Ordinal) + 2;
        CodeIntelligenceNavigationResult metadata = await engine.FindDefinitionAsync(
            InteractiveSnapshot(contextId, session.SessionId!, source, stringType));
        CodeIntelligenceNavigationResult unavailable = await engine.FindDefinitionAsync(
            InteractiveSnapshot(contextId, session.SessionId!, source, source.Length));

        Assert.Contains(quickInfo.Sections, section =>
            section.Value.Contains("Value", StringComparison.Ordinal));
        Assert.Contains(definition.Destinations, destination =>
            destination.Kind is CodeIntelligenceDestinationKind.Source &&
            destination.Path?.Value == "Sample.cs");
        Assert.Contains(references.Destinations, destination =>
            destination.Kind is CodeIntelligenceDestinationKind.Source);
        CodeIntelligenceSignatureItem signature = Assert.Single(signatures.Signatures);
        Assert.Contains("Run", signature.Display.Value, StringComparison.Ordinal);
        Assert.Equal("Runs one operation.", signature.Documentation.Value);
        Assert.Equal(2, signature.Parameters.Count);
        Assert.Equal("The repeat count.", signature.Parameters[1].Documentation.Value);
        Assert.Equal(0, signatures.SelectedParameter);
        Assert.Equal(1, secondParameter.SelectedParameter);
        Assert.Contains(metadata.Destinations, destination =>
            destination.Kind is CodeIntelligenceDestinationKind.Metadata);
        Assert.Contains(unavailable.Destinations, destination =>
            destination.Kind is CodeIntelligenceDestinationKind.Unavailable);
    }

    [Fact]
    public async Task Implementation_lookup_resolves_interface_and_override_sources()
    {
        const string source = """
            interface IRunner
            {
                void Run();
            }
            class BaseRunner
            {
                public virtual void Execute() { }
            }
            class Runner : BaseRunner, IRunner
            {
                public void Run() { }
                public override void Execute() { }
            }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("implementations-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceNavigationResult interfaceImplementations =
            await engine.FindImplementationsAsync(InteractiveSnapshot(
                contextId,
                session.SessionId!,
                source,
                source.IndexOf("IRunner", StringComparison.Ordinal) + 2));
        CodeIntelligenceNavigationResult overrideImplementations =
            await engine.FindImplementationsAsync(InteractiveSnapshot(
                contextId,
                session.SessionId!,
                source,
                source.IndexOf("Execute()", StringComparison.Ordinal) + 2));

        Assert.Contains(interfaceImplementations.Destinations, destination =>
            destination.Kind is CodeIntelligenceDestinationKind.Source &&
            destination.Display.Value.Contains("Runner", StringComparison.Ordinal));
        Assert.Contains(overrideImplementations.Destinations, destination =>
            destination.Kind is CodeIntelligenceDestinationKind.Source &&
            destination.Display.Value.Contains("Runner.Execute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Semantic_graphs_search_calls_types_and_associated_tests_with_paging()
    {
        const string source = """
            class BaseRunner { public virtual void Run() { } }
            class Runner : BaseRunner
            {
                public override void Run() { Helper(); }
                void Helper() { }
                [Fact] void Run_is_callable() { Run(); }
            }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("semantic-graphs-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        CodeIntelligenceInteractiveSnapshot runner = InteractiveSnapshot(contextId, session.SessionId!,
            source, source.IndexOf("Runner :", StringComparison.Ordinal) + 2);
        CodeIntelligenceInteractiveSnapshot run = InteractiveSnapshot(contextId, session.SessionId!,
            source, source.IndexOf("Run() { Helper", StringComparison.Ordinal) + 2);

        CodeIntelligenceSemanticResult symbols = await engine.SearchSymbolsAsync(
            new(runner, "Run", 1, 0));
        CodeIntelligenceSemanticResult calls = await engine.AnalyzeCallsAsync(new(run, null, 20, 0));
        CodeIntelligenceSemanticResult types = await engine.GetTypeHierarchyAsync(new(runner, null, 20, 0));
        CodeIntelligenceSemanticResult tests = await engine.FindAssociatedTestsAsync(new(run, null, 20, 0));

        Assert.Single(symbols.Items);
        Assert.True(symbols.IsTruncated);
        Assert.NotNull(symbols.Continuation);
        Assert.Contains(calls.Items, item => item.Relation is CodeIntelligenceSemanticRelation.OutgoingCall &&
            item.Display.Value.Contains("Helper", StringComparison.Ordinal));
        Assert.Contains(types.Items, item => item.Relation is CodeIntelligenceSemanticRelation.BaseType &&
            item.Display.Value.Contains("BaseRunner", StringComparison.Ordinal));
        Assert.Contains(tests.Items, item => item.Relation is CodeIntelligenceSemanticRelation.AssociatedTest);
    }

    [Fact]
    public async Task Document_presentation_and_occurrences_use_the_exact_live_buffer()
    {
        const string persisted = "class Sample { }\n";
        const string live = """
            namespace Demo;

            class Sample
            {
                private int count;

                #region Behavior
                public int Added(int amount)
                {
                    count += amount;
                    return count;
                }

                public void Use()
                {
                    var total = Added(2);
                }
                #endregion
            }
            """;
        await CreateProjectAsync(persisted);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("presentation-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = live.LastIndexOf("count", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot = InteractiveSnapshot(
            contextId, session.SessionId!, persisted, live, offset);

        CodeIntelligenceDocumentPresentationResult presentation =
            await engine.GetDocumentPresentationAsync(new(
                snapshot,
                VisibleRange: null,
                InlayHints: new(true, true),
                CodeLens: new(true, true, true)));
        CodeIntelligenceDocumentPresentationResult visible =
            await engine.GetDocumentPresentationAsync(new(snapshot,
                new(new(6, 0), new(10, 1))));
        CodeIntelligenceDocumentPresentationResult classificationOnly =
            await engine.GetDocumentPresentationAsync(new(
                snapshot,
                new(new(6, 0), new(10, 1)),
                CodeIntelligenceDocumentPresentationScope.VisibleClassification));
        CodeIntelligenceOccurrenceResult occurrences =
            await engine.FindOccurrencesAsync(snapshot);

        Assert.Equal(CodeIntelligenceResultState.Ready, presentation.State);
        Assert.Contains(presentation.Classifications, item =>
            item.Kind is CodeIntelligenceClassificationKind.Type);
        Assert.Contains(presentation.Classifications, item =>
            item.Kind is CodeIntelligenceClassificationKind.Method);
        Assert.Contains(presentation.Outline, item =>
            item.Kind is CodeIntelligenceSymbolKind.Method &&
            item.Display.Value.StartsWith("Added", StringComparison.Ordinal));
        Assert.Contains(presentation.Outline, item =>
            item.Kind is CodeIntelligenceSymbolKind.Region &&
            item.Display.Value == "#region Behavior");
        Assert.Contains(presentation.FoldingRanges, item =>
            item.Kind is CodeIntelligenceFoldingKind.Type);
        Assert.Contains(presentation.FoldingRanges, item =>
            item.Kind is CodeIntelligenceFoldingKind.Region);
        Assert.NotEmpty(visible.Classifications);
        Assert.All(visible.Classifications, item =>
            Assert.InRange(item.Range.Start.Line, 6, 10));
        Assert.Contains(visible.Outline, item => item.Display.Value == "Sample");
        Assert.NotEmpty(classificationOnly.Classifications);
        Assert.Empty(classificationOnly.Outline);
        Assert.Empty(classificationOnly.FoldingRanges);
        Assert.Collection(
            presentation.Breadcrumbs,
            item => Assert.Equal("Demo", item.Display.Value),
            item => Assert.Equal("Sample", item.Display.Value),
            item => Assert.StartsWith("Added", item.Display.Value, StringComparison.Ordinal));
        Assert.Contains(occurrences.Occurrences, item =>
            item.Kind is CodeIntelligenceOccurrenceKind.Definition);
        Assert.Contains(occurrences.Occurrences, item =>
            item.Kind is CodeIntelligenceOccurrenceKind.Write);
        Assert.Contains(occurrences.Occurrences, item =>
            item.Kind is CodeIntelligenceOccurrenceKind.Read);
        Assert.Contains("count", occurrences.Symbol!.Value, StringComparison.Ordinal);
        Assert.Contains(presentation.InlayHints, item =>
            item.Kind is CodeIntelligenceInlayHintKind.ParameterName &&
            item.Label.Value == "amount:");
        Assert.Contains(presentation.InlayHints, item =>
            item.Kind is CodeIntelligenceInlayHintKind.InferredType &&
            item.Label.Value.Contains("int", StringComparison.Ordinal));
        Assert.Contains(presentation.CodeLenses, item =>
            item.Kind is CodeIntelligenceCodeLensKind.References && !item.IsResolved);
        Assert.Contains(presentation.CodeLenses, item =>
            item.Kind is CodeIntelligenceCodeLensKind.Tests && !item.IsResolved);
    }

    [Fact]
    public async Task Entry_point_lenses_carry_a_confined_typed_execution_target()
    {
        const string source = "class Program\n{\n    public static void Main() { }\n}\n";
        await CreateProjectAsync(source);
        string projectPath = Path.Combine(root, "Sample.csproj");
        string project = await File.ReadAllTextAsync(projectPath);
        await File.WriteAllTextAsync(projectPath, project.Replace(
            "<TargetFramework>net10.0</TargetFramework>",
            "<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>",
            StringComparison.Ordinal));
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("entry-point-lens-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        CodeIntelligenceInteractiveSnapshot snapshot = InteractiveSnapshot(
            contextId, session.SessionId!, source, source.IndexOf("Main", StringComparison.Ordinal));

        CodeIntelligenceDocumentPresentationResult presentation =
            await engine.GetDocumentPresentationAsync(new(
                snapshot,
                VisibleRange: new(new(0, 0), new(0, 5)),
                CodeLens: new(false, false, false, ShowRun: true, ShowDebug: true)));

        CodeIntelligenceCodeLens run = Assert.Single(presentation.CodeLenses,
            item => item.Kind is CodeIntelligenceCodeLensKind.Run);
        CodeIntelligenceCodeLens debug = Assert.Single(presentation.CodeLenses,
            item => item.Kind is CodeIntelligenceCodeLensKind.Debug);
        Assert.True(run.IsResolved);
        Assert.Equal(run.ExecutionTarget, debug.ExecutionTarget);
        Assert.Equal(CodeIntelligenceExecutionTargetKind.ProjectEntryPoint,
            run.ExecutionTarget?.Kind);
        Assert.Equal("Sample.csproj", run.ExecutionTarget?.ProjectPath.Value);
        Assert.Equal("net10.0", run.ExecutionTarget?.TargetFramework.Value);
        Assert.Contains("Program.Main", run.ExecutionTarget?.DeclarationId.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_preview_resolves_a_partial_type_across_files_without_writing()
    {
        const string declaration = "public partial class Widget { public void Run() { } }\n";
        const string use = "public partial class Widget { }\nclass Use { Widget value = new(); }\n";
        await CreateProjectAsync(declaration);
        await File.WriteAllTextAsync(Path.Combine(root, "Use.cs"), use, Utf8WithoutBom);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("rename-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = declaration.IndexOf("Widget", StringComparison.Ordinal) + 2;

        CodeIntelligenceRenamePreviewResult result = await engine.PreviewRenameAsync(new(
            InteractiveSnapshot(contextId, session.SessionId!, declaration, offset),
            new("Gadget")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.NotNull(result.Symbol);
        Assert.NotNull(result.Fingerprint);
        Assert.Equal(2, result.Edits.Count);
        Assert.All(result.Edits, edit => Assert.Contains("Gadget", edit.Text.Value, StringComparison.Ordinal));
        Assert.Contains(result.Edits, edit => edit.OriginalText.Value == declaration);
        Assert.Contains(result.Edits, edit => edit.OriginalText.Value == use);
        Assert.Equal(declaration, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
        Assert.Equal(use, await File.ReadAllTextAsync(Path.Combine(root, "Use.cs")));
    }

}
