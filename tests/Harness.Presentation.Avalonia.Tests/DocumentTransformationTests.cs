using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Editor_rename_uses_the_shared_fingerprinted_atomic_operation()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            MutationService mutations = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                mutationService: mutations);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            editor.CaretOffset = editor.Text.IndexOf("Example", StringComparison.Ordinal) + 2;

            PendingWorkbenchRename pending = Assert.IsType<PendingWorkbenchRename>(
                workbench.PreviewActiveRenameAsync("Renamed").AsTask().GetAwaiter().GetResult());
            RenameSymbolApplyView applied = Assert.IsType<RenameSymbolApplyView>(
                workbench.ApplyActiveRenameAsync(pending).AsTask().GetAwaiter().GetResult());

            Assert.Equal("Renamed", pending.Preview.NewName.Value);
            Assert.Equal("goal-1", mutations.PreviewRequest?.GoalId);
            Assert.Equal("src/App.cs", mutations.PreviewRequest?.Path.Value);
            Assert.Equal(1, mutations.ApplyCallCount);
            Assert.Null(applied.ErrorCode);
            Assert.Equal("namespace Renamed;", editor.Text);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_format_document_applies_one_undoable_live_buffer_change()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [new(
                        request.Snapshot.Path,
                        request.Snapshot.BaselineHash,
                        request.Snapshot.Text,
                        new("namespace Example;\n"),
                        1)],
                    [],
                    [],
                    new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            string original = editor.Text;

            workbench.TransformActiveDocumentAsync(
                WorkbenchCodeDocumentTransformationKind.FormatDocument)
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal("namespace Example;\n", editor.Text);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            editor.Document.UndoStack.Undo();
            Assert.Equal(original, editor.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_formats_pasted_text_through_the_guarded_Roslyn_preview()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            WorkbenchCodeDocumentTransformationPreviewRequest? observed = null;
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request =>
                {
                    observed = request;
                    return new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        WorkbenchCodeTransformationDisposition.Ready,
                        request.Kind,
                        request.Range,
                        [new(
                            request.Snapshot.Path,
                            request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new("namespace Example;\n"),
                            1)],
                        [],
                        [],
                        new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                        [],
                        ImportNamespace: null,
                        FormattingTrigger: request.FormattingTrigger);
                },
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            editor.Text += "abc";
            editor.CaretOffset = editor.Text.Length;

            workbench.HandleActivePasteAsync(new(new(0, 18), new(0, 21)))
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal(WorkbenchCodeDocumentTransformationKind.FormatPaste, observed?.Kind);
            Assert.Equal(WorkbenchCodeFormattingTrigger.Paste, observed?.FormattingTrigger);
            Assert.NotNull(observed?.Range);
            Assert.Equal("namespace Example;\n", editor.Text);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_quick_fix_discovers_typed_missing_import_choices_at_the_caret()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                MissingImports = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(new("System.Text"), new("System.Text.StringBuilder"),
                        new(new(0, 0), new(0, 7)))],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.ShowActiveQuickFixesAsync().AsTask().GetAwaiter().GetResult();

            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Assert.Contains(sourceContent.GetVisualDescendants().OfType<Button>(), button =>
                button.Content?.ToString() == "Quick fix…" &&
                AutomationProperties.GetName(button)?.StartsWith(
                    "Show quick fixes", StringComparison.Ordinal) is true);
            Assert.Contains(sourceContent.GetLogicalDescendants().OfType<TextBlock>(), block =>
                block.Text?.Contains("1 Roslyn quick fix", StringComparison.Ordinal) is true);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_applies_a_closed_code_action_as_one_undoable_buffer_change()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            WorkbenchCodeDocumentTransformationPreviewRequest? observed = null;
            const string actionId =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            CodeIntelligenceService codeIntelligence = new()
            {
                CodeActions = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(new(actionId), WorkbenchClosedCodeActionKind.ImplementInterface,
                        WorkbenchCodeActionScope.Occurrence, new("Implement interface"),
                        new("CS0535"), new(new(0, 0), new(0, 7)))],
                    []),
                DocumentTransformations = request =>
                {
                    observed = request;
                    return new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        WorkbenchCodeTransformationDisposition.Ready,
                        request.Kind,
                        request.Range,
                        [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new(request.Snapshot.Text.Value + "void Run() { }\n"), 1)],
                        [], [], new(actionId), [],
                        CodeActionId: request.CodeActionId,
                        CodeActionScope: request.CodeActionScope);
                },
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            string original = editor.Text;

            WorkbenchCodeActionCandidate candidate = new(
                new(actionId), WorkbenchClosedCodeActionKind.ImplementInterface,
                WorkbenchCodeActionScope.Occurrence, new("Implement interface"),
                new("CS0535"), new(new(0, 0), new(0, 7)));
            workbench.ApplyActiveCodeActionAsync(candidate).AsTask().GetAwaiter().GetResult();

            Assert.Equal(WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                observed?.Kind);
            Assert.Equal(actionId, observed?.CodeActionId?.Value);
            Assert.Equal(WorkbenchCodeActionScope.Occurrence, observed?.CodeActionScope);
            Assert.Contains("void Run()", editor.Text, StringComparison.Ordinal);
            editor.Document.UndoStack.Undo();
            Assert.Equal(original, editor.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_routes_a_cross_document_code_action_through_atomic_goal_mutation()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            const string actionId =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            MutationService mutations = new();
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [
                        new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new(request.Snapshot.Text.Value + "// transformed\n"), 1),
                        new(new("src/Other.cs"), request.Snapshot.BaselineHash,
                            new("class Other { }\n"),
                            new("class Other { void Changed() { } }\n"), 1),
                    ],
                    [],
                    [],
                    new(actionId),
                    [],
                    CodeActionId: request.CodeActionId,
                    CodeActionScope: request.CodeActionScope),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence,
                mutationService: mutations);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.ApplyActiveCodeActionAsync(new(
                new(actionId),
                WorkbenchClosedCodeActionKind.ReplaceMemberKind,
                WorkbenchCodeActionScope.Occurrence,
                new("Replace property with methods"),
                DiagnosticId: null,
                new(new(0, 0), new(0, 7)))).AsTask().GetAwaiter().GetResult();

            Assert.Equal(1, mutations.DocumentApplyCallCount);
            Assert.Equal(WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                mutations.DocumentApplyRequest?.PreviewRequest.Kind);
            Assert.Contains("// transformed", workbench.ActiveSourceEditor?.Text,
                StringComparison.Ordinal);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_blocks_cross_document_action_when_an_affected_open_file_is_dirty()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            const string actionId =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            MutationService mutations = new();
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [
                        new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new(request.Snapshot.Text.Value + "// transformed\n"), 1),
                        new(new("src/Other.cs"), request.Snapshot.BaselineHash,
                            new("namespace Example;"),
                            new("namespace Changed;"), 1),
                    ],
                    [], [], new(actionId), [],
                    CodeActionId: request.CodeActionId,
                    CodeActionScope: request.CodeActionScope),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence,
                mutationService: mutations);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "namespace Unsaved;";
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            string activeBefore = workbench.ActiveSourceEditor!.Text;

            workbench.ApplyActiveCodeActionAsync(new(
                new(actionId), WorkbenchClosedCodeActionKind.ReplaceMemberKind,
                WorkbenchCodeActionScope.Occurrence,
                new("Replace property with methods"), DiagnosticId: null,
                new(new(0, 0), new(0, 7)), AffectedFileCount: 2))
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal(0, mutations.DocumentApplyCallCount);
            Assert.Equal(activeBefore, workbench.ActiveSourceEditor.Text);
            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Assert.Contains(sourceContent.GetLogicalDescendants().OfType<TextBlock>(), block =>
                block.Text?.Contains("Save or revert unsaved changes in src/Other.cs",
                    StringComparison.Ordinal) is true);
            window.Close();
        }, CancellationToken.None);
    }

}
