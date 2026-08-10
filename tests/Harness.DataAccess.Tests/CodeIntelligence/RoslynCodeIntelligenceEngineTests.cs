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

    private async ValueTask CreateLinkedSolutionAsync(string shared)
    {
        Directory.CreateDirectory(Path.Combine(root, "First"));
        Directory.CreateDirectory(Path.Combine(root, "Second"));
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), """
            {
              "sdk": {
                "version": "10.0.201",
                "rollForward": "latestPatch",
                "allowPrerelease": false
              }
            }
            """);
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
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), """
            {
              "sdk": {
                "version": "10.0.201",
                "rollForward": "latestPatch",
                "allowPrerelease": false
              }
            }
            """);
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
