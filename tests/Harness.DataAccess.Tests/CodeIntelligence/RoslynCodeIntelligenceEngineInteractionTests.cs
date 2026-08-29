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
    public async Task Format_document_previews_complete_Roslyn_edits_without_writing()
    {
        const string source = "class Sample{void Run(){int value=1;}}\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("format-document-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                CodeIntelligenceDocumentTransformationKind.FormatDocument,
                Range: null));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.NotNull(result.Fingerprint);
        CodeIntelligenceDocumentTransformationEdit edit = Assert.IsType<
            CodeIntelligenceDocumentTransformationEdit>(result.Edit);
        Assert.True(edit.ReplacementCount > 0);
        Assert.Contains("class Sample { void Run() { int value = 1; } }",
            edit.Text.Value, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Format_selection_changes_only_the_requested_member()
    {
        const string source = "class Sample\n{\n    void First(){int value=1;}\n    void Second(){int value=2;}\n}\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("format-selection-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                CodeIntelligenceDocumentTransformationKind.FormatSelection,
                new(new(2, 4), new(2, 37))));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.Contains("void First()", result.Edit!.Text.Value, StringComparison.Ordinal);
        Assert.Contains("void Second(){int value=2;}", result.Edit.Text.Value, StringComparison.Ordinal);
        Assert.NotNull(result.Range);
    }

    [Fact]
    public async Task Format_changed_spans_leaves_unchanged_members_alone()
    {
        const string persisted = "class Sample\n{\n    void First() { int value = 1; }\n    void Second(){int value=2;}\n}\n";
        const string current = "class Sample\n{\n    void First(){int value=3;}\n    void Second(){int value=2;}\n}\n";
        await CreateProjectAsync(persisted);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("format-changed-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, persisted, current, 0),
                CodeIntelligenceDocumentTransformationKind.FormatChangedSpans,
                Range: null));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.Contains("void First() { int value = 3; }", result.Edit!.Text.Value,
            StringComparison.Ordinal);
        Assert.Contains("void Second(){int value=2;}", result.Edit.Text.Value,
            StringComparison.Ordinal);
        Assert.Equal(persisted, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Theory]
    [InlineData(CodeIntelligenceDocumentTransformationKind.FormatPaste,
        CodeIntelligenceFormattingTrigger.Paste)]
    [InlineData(CodeIntelligenceDocumentTransformationKind.FormatOnType,
        CodeIntelligenceFormattingTrigger.Semicolon)]
    public async Task Triggered_formatting_is_confined_to_the_exact_line(
        CodeIntelligenceDocumentTransformationKind kind,
        CodeIntelligenceFormattingTrigger trigger)
    {
        const string persisted = "class Sample\n{\n    void First() { }\n    void Second(){int value=2;}\n}\n";
        const string current = "class Sample\n{\n    void First(){int value=1;}\n    void Second(){int value=2;}\n}\n";
        await CreateProjectAsync(persisted);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new($"format-trigger-{trigger}");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, persisted, current, 0),
                kind,
                new(new(2, 4), new(2, 30)),
                ImportNamespace: null,
                FormattingTrigger: trigger));

        Assert.True(result.Disposition is CodeIntelligenceTransformationDisposition.Ready,
            string.Join(" | ", result.Issues.Select(item =>
                $"{item.Code.Value}: {item.Message.Value}")));
        Assert.Equal(trigger, result.FormattingTrigger);
        Assert.Contains("void First() { int value = 1; }", result.Edit!.Text.Value,
            StringComparison.Ordinal);
        Assert.Contains("void Second(){int value=2;}", result.Edit.Text.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Organize_imports_sorts_directives_and_preserves_source_on_disk()
    {
        const string source = "using System.Text;\nusing System;\nclass Sample { StringBuilder Value = new(); }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("organize-imports-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                CodeIntelligenceDocumentTransformationKind.OrganizeImports,
                Range: null));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.StartsWith("using System;\nusing System.Text;", result.Edit!.Text.Value,
            StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Remove_unused_imports_uses_Roslyn_diagnostics_without_writing()
    {
        const string source = "using System.Text;\nusing System;\nclass Sample { void Run() { Console.WriteLine(); } }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("remove-unused-imports-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                CodeIntelligenceDocumentTransformationKind.RemoveUnusedImports,
                Range: null));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.DoesNotContain("System.Text", result.Edit!.Text.Value, StringComparison.Ordinal);
        Assert.Contains("using System;", result.Edit.Text.Value, StringComparison.Ordinal);
        Assert.NotNull(result.Fingerprint);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Remove_unused_imports_preserves_a_directive_with_attached_comments()
    {
        const string source = "// Why this import remains visible\nusing System.Text;\nclass Sample { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("commented-unused-import-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                CodeIntelligenceDocumentTransformationKind.RemoveUnusedImports,
                Range: null));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, result.Disposition);
        Assert.Equal(source, result.Edit!.Text.Value);
        Assert.Equal(0, result.Edit.ReplacementCount);
    }

    [Fact]
    public async Task Missing_import_discovery_returns_only_a_namespace_that_binds_the_type()
    {
        const string source = "class Sample { StringBuilder Value = new(); }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("missing-import-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("StringBuilder", StringComparison.Ordinal) + 3;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceMissingImportResult discovery =
            await engine.GetMissingImportsAsync(snapshot);
        CodeIntelligenceMissingImportCandidate candidate = Assert.Single(discovery.Candidates,
            item => item.Namespace.Value == "System.Text");
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.AddMissingImport,
                Range: null,
                candidate.Namespace));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        Assert.StartsWith("using System.Text;", preview.Edit!.Text.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(preview.Diagnostics, item =>
            item.Kind is CodeIntelligenceDiagnosticDeltaKind.Retained &&
            item.Diagnostic.Id.Value == "CS0246");
        Assert.NotNull(preview.Fingerprint);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Add_missing_import_rejects_a_namespace_that_was_not_discovered()
    {
        const string source = "class Sample { StringBuilder Value = new(); }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("invalid-import-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("StringBuilder", StringComparison.Ordinal) + 3;

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, offset),
                CodeIntelligenceDocumentTransformationKind.AddMissingImport,
                Range: null,
                new("System.IO")));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Rejected, result.Disposition);
        Assert.Equal("missing_import_candidate_changed", Assert.Single(result.Issues).Code.Value);
        Assert.Null(result.Fingerprint);
    }

    [Fact]
    public async Task Closed_code_action_discovers_and_previews_interface_implementation_without_writing()
    {
        const string source = "interface IWorker { void Run(); }\nclass Worker : IWorker { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("implement-interface-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.LastIndexOf("Worker", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceCodeActionResult discovery = await engine.GetCodeActionsAsync(new(snapshot));
        CodeIntelligenceCodeActionCandidate candidate = Assert.Single(
            discovery.Candidates,
            item => item.Kind is CodeIntelligenceClosedCodeActionKind.ImplementInterface &&
                item.Scope is CodeIntelligenceCodeActionScope.Occurrence &&
                item.Title.Value.Equals("Implement interface", StringComparison.Ordinal));
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        Assert.Equal(candidate.Id, preview.CodeActionId);
        Assert.Equal(candidate.Scope, preview.CodeActionScope);
        Assert.Contains("void Run()", preview.Edit!.Text.Value, StringComparison.Ordinal);
        Assert.NotNull(preview.Fingerprint);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Closed_code_action_rejects_an_unknown_identifier()
    {
        const string source = "interface IWorker { void Run(); }\nclass Worker : IWorker { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("unknown-code-action-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.LastIndexOf("Worker", StringComparison.Ordinal) + 2;

        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, offset),
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: new(new string('a', 64)),
                CodeActionScope: CodeIntelligenceCodeActionScope.Occurrence));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Rejected, preview.Disposition);
        Assert.Equal("code_action_changed", Assert.Single(preview.Issues).Code.Value);
        Assert.Null(preview.Fingerprint);
    }

    [Fact]
    public async Task Closed_document_code_action_fixes_every_matching_diagnostic()
    {
        const string source = "partial class Widget { }\nclass Widget { }\n" +
            "partial class Gadget { }\nclass Gadget { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("document-fix-all-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.LastIndexOf("class Widget", StringComparison.Ordinal) + 7;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceCodeActionResult discovery = await engine.GetCodeActionsAsync(new(snapshot));
        CodeIntelligenceCodeActionCandidate candidate = Assert.Single(
            discovery.Candidates,
            item => item.Kind is CodeIntelligenceClosedCodeActionKind.MakeTypePartial &&
                item.Scope is CodeIntelligenceCodeActionScope.Document);
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        Assert.Equal(4, preview.Edit!.Text.Value.Split("partial class").Length - 1);
        Assert.DoesNotContain(preview.Diagnostics, item =>
            item.Kind is CodeIntelligenceDiagnosticDeltaKind.Retained &&
            item.Diagnostic.Id.Value == "CS0260");
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Closed_refactoring_previews_an_auto_property_conversion_without_writing()
    {
        const string source = "class Sample { public int Value { get; set; } }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("auto-property-refactoring-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = source.IndexOf("Value", StringComparison.Ordinal) + 2;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, offset);

        CodeIntelligenceCodeActionResult discovery = await engine.GetCodeActionsAsync(new(snapshot));
        CodeIntelligenceCodeActionCandidate candidate = Assert.Single(
            discovery.Candidates,
            item => item.Kind is
                CodeIntelligenceClosedCodeActionKind.ConvertAutoPropertyToFullProperty &&
                item.Title.Value.Equals("Convert to full property", StringComparison.Ordinal));
        Assert.Null(candidate.DiagnosticId);
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        Assert.Contains("private int", preview.Edit!.Text.Value, StringComparison.Ordinal);
        Assert.Contains("get =>", preview.Edit.Text.Value, StringComparison.Ordinal);
        Assert.NotNull(preview.Fingerprint);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Closed_add_parameter_action_previews_its_cross_document_edit_without_writing()
    {
        const string target = "public class Target { public void Run(int value) { } }\n";
        const string use = "class Use { void Go() { new Target().Run(1, 2); } }\n";
        await CreateProjectAsync(target);
        await File.WriteAllTextAsync(Path.Combine(root, "Use.cs"), use, Utf8WithoutBom);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("cross-document-action-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = use.IndexOf("Run", StringComparison.Ordinal) + 1;
        CodeIntelligenceInteractiveSnapshot snapshot = InteractiveSnapshot(
            contextId, session.SessionId!, use, use, offset, "Use.cs");

        CodeIntelligenceCodeActionResult discovery = await engine.GetCodeActionsAsync(new(snapshot));
        CodeIntelligenceCodeActionCandidate candidate = Assert.Single(
            discovery.Candidates,
            item => item.Kind is CodeIntelligenceClosedCodeActionKind.AddParameter);
        Assert.Equal(1, candidate.AffectedFileCount);
        Assert.False(candidate.ChangesActiveDocument);
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        CodeIntelligenceDocumentTransformationEdit edit = Assert.Single(preview.Edits);
        Assert.Equal("Sample.cs", edit.Path.Value);
        Assert.Equal(target, edit.OriginalText.Value);
        Assert.Contains("Run(int value, int", edit.Text.Value, StringComparison.Ordinal);
        Assert.NotNull(preview.Fingerprint);
        Assert.Equal(target, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
        Assert.Equal(use, await File.ReadAllTextAsync(Path.Combine(root, "Use.cs")));
    }

    [Fact]
    public async Task Closed_member_replacement_previews_every_affected_document()
    {
        const string target =
            "public class Target { public int Value { get; set; } }\n";
        const string use =
            "class Use { int Read(Target target) { target.Value = 3; return target.Value; } }\n";
        await CreateProjectAsync(target);
        await File.WriteAllTextAsync(Path.Combine(root, "Use.cs"), use, Utf8WithoutBom);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("multi-document-refactoring-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int offset = target.IndexOf("Value", StringComparison.Ordinal) + 1;
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, target, offset);

        CodeIntelligenceCodeActionResult discovery = await engine.GetCodeActionsAsync(new(snapshot));
        CodeIntelligenceCodeActionCandidate candidate = Assert.Single(
            discovery.Candidates,
            item => item.Kind is CodeIntelligenceClosedCodeActionKind.ReplaceMemberKind);
        Assert.Equal(2, candidate.AffectedFileCount);
        Assert.True(candidate.ChangesActiveDocument);
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                Range: null,
                CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        Assert.Equal(["Sample.cs", "Use.cs"],
            preview.Edits.Select(edit => edit.Path.Value).ToArray());
        Assert.All(preview.Edits, edit => Assert.True(edit.ReplacementCount > 0));
        Assert.Contains("GetValue", preview.Edits[0].Text.Value, StringComparison.Ordinal);
        Assert.Contains("SetValue", preview.Edits[1].Text.Value, StringComparison.Ordinal);
        Assert.NotNull(preview.Fingerprint);
        Assert.Equal(target, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
        Assert.Equal(use, await File.ReadAllTextAsync(Path.Combine(root, "Use.cs")));
    }

    [Fact]
    public async Task Closed_selection_refactoring_uses_the_exact_selected_expression()
    {
        const string source = "class Sample { int Run() { return 1 + 2; } }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("selection-refactoring-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));
        int start = source.IndexOf("1 + 2", StringComparison.Ordinal);
        CodeIntelligenceRange range = new(new(0, start), new(0, start + 5));
        CodeIntelligenceInteractiveSnapshot snapshot =
            InteractiveSnapshot(contextId, session.SessionId!, source, start);

        CodeIntelligenceCodeActionResult discovery = await engine.GetCodeActionsAsync(
            new(snapshot, range));
        CodeIntelligenceCodeActionCandidate candidate = Assert.Single(
            discovery.Candidates,
            item => item.Kind is CodeIntelligenceClosedCodeActionKind.ExtractMethod &&
                item.Title.Value.Equals("Extract method", StringComparison.Ordinal));
        CodeIntelligenceDocumentTransformationPreviewResult preview =
            await engine.PreviewDocumentTransformationAsync(new(
                snapshot,
                CodeIntelligenceDocumentTransformationKind.ApplyCodeAction,
                range,
                CodeActionId: candidate.Id,
                CodeActionScope: candidate.Scope));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Ready, preview.Disposition);
        Assert.Contains("return", preview.Edit!.Text.Value, StringComparison.Ordinal);
        Assert.Contains("1 + 2", preview.Edit.Text.Value, StringComparison.Ordinal);
        Assert.Contains("NewMethod", preview.Edit.Text.Value, StringComparison.Ordinal);
        Assert.True(preview.Edit.ReplacementCount > 0);
        Assert.Equal(source, await File.ReadAllTextAsync(Path.Combine(root, "Sample.cs")));
    }

    [Fact]
    public async Task Document_transformation_rejects_a_range_for_organize_imports()
    {
        const string source = "using System;\nclass Sample { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("invalid-transform-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                CodeIntelligenceDocumentTransformationKind.OrganizeImports,
                new(new(0, 0), new(0, 5))));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Rejected, result.Disposition);
        Assert.Equal("invalid_document_transformation", Assert.Single(result.Issues).Code.Value);
        Assert.Null(result.Fingerprint);
    }

    [Theory]
    [InlineData(CodeIntelligenceDocumentTransformationKind.FormatPaste,
        CodeIntelligenceFormattingTrigger.Semicolon)]
    [InlineData(CodeIntelligenceDocumentTransformationKind.FormatOnType,
        CodeIntelligenceFormattingTrigger.Paste)]
    public async Task Triggered_formatting_rejects_a_mismatched_trigger(
        CodeIntelligenceDocumentTransformationKind kind,
        CodeIntelligenceFormattingTrigger trigger)
    {
        const string source = "class Sample { }\n";
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new($"invalid-trigger-{kind}");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceDocumentTransformationPreviewResult result =
            await engine.PreviewDocumentTransformationAsync(new(
                InteractiveSnapshot(contextId, session.SessionId!, source, 0),
                kind,
                new(new(0, 0), new(0, 1)),
                ImportNamespace: null,
                FormattingTrigger: trigger));

        Assert.Equal(CodeIntelligenceTransformationDisposition.Rejected, result.Disposition);
        Assert.Equal("invalid_document_transformation", Assert.Single(result.Issues).Code.Value);
        Assert.Null(result.Fingerprint);
    }

}
