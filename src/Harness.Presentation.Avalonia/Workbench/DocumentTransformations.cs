using Avalonia.Automation;
using Avalonia.Controls;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class DocumentTransformations
{
    private readonly IWorkspaceMutationService? mutations;
    private readonly IWorkbenchCodeIntelligenceService code;
    private readonly DocumentIntelligence intelligence;
    private readonly DocumentInteractions interactions;
    private readonly Func<IReadOnlyDictionary<string, SourceDocumentSession>> documents;
    private readonly Func<ValueTask> invalidate;
    private readonly CancellationToken cancellationToken;

    internal DocumentTransformations(
        IWorkspaceMutationService? mutations,
        IWorkbenchCodeIntelligenceService code,
        DocumentIntelligence intelligence,
        DocumentInteractions interactions,
        Func<IReadOnlyDictionary<string, SourceDocumentSession>> documents,
        Func<ValueTask> invalidate,
        CancellationToken cancellationToken)
    {
        this.mutations = mutations;
        this.code = code;
        this.intelligence = intelligence;
        this.interactions = interactions;
        this.documents = documents;
        this.invalidate = invalidate;
        this.cancellationToken = cancellationToken;
    }

    internal bool CanTransform(
        SourceDocumentSession document,
        WorkbenchCodeDocumentTransformationKind kind) =>
        document.View.Access is WorkbenchDocumentAccess.Editable &&
        DocumentIntelligence.CanUse(document) &&
        (kind is not WorkbenchCodeDocumentTransformationKind.FormatSelection ||
            document.Editor.SelectionRange is not null);

    internal ValueTask HandlePasteAsync(
        SourceDocumentSession document,
        WorkbenchCodeRange range) => intelligence.Preferences.FormatOnPaste
        ? TransformAsync(
            document,
            WorkbenchCodeDocumentTransformationKind.FormatPaste,
            range: range,
            formattingTrigger: WorkbenchCodeFormattingTrigger.Paste,
            automatic: true)
        : ValueTask.CompletedTask;

    internal async ValueTask HandleTextEnteredAsync(
        SourceDocumentSession document,
        string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 1) return;
        char value = text[0];
        if (value is '(' or ',') await interactions.ShowSignatureHelpAsync(document);
        else if (value == ')')
        {
            document.SignatureWindow?.Hide();
            document.SignatureWindow = null;
        }
        if (char.IsLetterOrDigit(value) || value is '_' or '.')
            await interactions.ShowCompletionAsync(
                document, WorkbenchCodeCompletionTriggerKind.Insertion, value);
        if (intelligence.Preferences.FormatOnType && FormattingTrigger(value) is { } trigger)
        {
            int end = document.Editor.CaretOffset;
            int start = Math.Max(0, end - 1);
            await TransformAsync(
                document,
                WorkbenchCodeDocumentTransformationKind.FormatOnType,
                range: new(document.Editor.GetPosition(start), document.Editor.GetPosition(end)),
                formattingTrigger: trigger,
                automatic: true);
        }
    }

    internal async ValueTask TransformAsync(
        SourceDocumentSession document,
        WorkbenchCodeDocumentTransformationKind kind,
        WorkbenchCodeImportNamespace? importNamespace = null,
        WorkbenchCodeRange? range = null,
        WorkbenchCodeFormattingTrigger? formattingTrigger = null,
        WorkbenchCodeActionId? codeActionId = null,
        WorkbenchCodeActionScope? codeActionScope = null,
        bool automatic = false)
    {
        if (!CanTransform(document, kind))
        {
            document.SetStatus("Formatting requires an editable C# source document.");
            return;
        }
        range ??= kind is WorkbenchCodeDocumentTransformationKind.FormatSelection
            ? document.Editor.SelectionRange
            : null;
        if (kind is WorkbenchCodeDocumentTransformationKind.FormatSelection && range is null)
        {
            document.SetStatus("Select the C# code to format first.");
            return;
        }
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        if (!automatic) document.SetBusy(true, BusyText(kind, importNamespace));
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null || !document.IsCurrentInteraction(version)) return;
            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                document, session, version);
            WorkbenchCodeDocumentTransformationPreviewView preview =
                await code.PreviewDocumentTransformationAsync(new(
                    snapshot, kind, range, importNamespace, formattingTrigger,
                    codeActionId, codeActionScope), token);
            if (!document.IsCurrentInteraction(version))
            {
                document.SetStatus("The buffer changed before the transformation could be applied.");
                return;
            }
            if (preview.Disposition is not WorkbenchCodeTransformationDisposition.Ready ||
                preview.Fingerprint is null || preview.Edits.Count == 0)
            {
                document.SetStatus(preview.Conflicts.FirstOrDefault()?.Message.Value ??
                    preview.Issues.FirstOrDefault()?.Message.Value ??
                    "Roslyn could not prepare the requested transformation.");
                return;
            }
            if (preview.Edits.Count != 1 ||
                !preview.Edits[0].Path.Value.Equals(document.View.Path.Value, StringComparison.Ordinal))
            {
                await ApplyAtomicAsync(
                    document, version, kind, range, importNamespace, formattingTrigger,
                    codeActionId, codeActionScope, preview, token);
                return;
            }
            WorkbenchCodeDocumentTransformationEdit edit = preview.Edits[0];
            if (!string.Equals(document.Editor.Text, edit.OriginalText.Value, StringComparison.Ordinal))
            {
                document.SetStatus("The buffer changed before the transformation could be applied.");
                return;
            }
            if (edit.ReplacementCount == 0)
            {
                document.SetStatus(NoChangeText(kind));
                return;
            }
            int caret = document.Editor.CaretOffset;
            document.Editor.Replace(0, document.Editor.TextLength, edit.Text.Value);
            document.Editor.CaretOffset = MapOffset(edit.OriginalText.Value, edit.Text.Value, caret);
            document.Editor.Focus();
            document.SetStatus(AppliedText(kind, edit.ReplacementCount, importNamespace, codeActionScope));
            intelligence.ScheduleDiagnostics(document, immediate: true);
            intelligence.SchedulePresentation(document, immediate: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            document.SetStatus("Document transformation cancelled.");
        }
        finally
        {
            if (!automatic) document.SetBusy(false);
        }
    }

    private async ValueTask ApplyAtomicAsync(
        SourceDocumentSession document,
        WorkbenchCodeBufferVersion version,
        WorkbenchCodeDocumentTransformationKind kind,
        WorkbenchCodeRange? range,
        WorkbenchCodeImportNamespace? importNamespace,
        WorkbenchCodeFormattingTrigger? formattingTrigger,
        WorkbenchCodeActionId? codeActionId,
        WorkbenchCodeActionScope? codeActionScope,
        WorkbenchCodeDocumentTransformationPreviewView preview,
        CancellationToken token)
    {
        if (mutations is null || document.View.GoalId is null || document.View.Sha256 is null)
        {
            document.SetStatus(
                "This refactoring changes another file and requires an approved goal worktree.");
            return;
        }
        WorkbenchCodeDocumentTransformationEdit? dirty = preview.Edits.FirstOrDefault(edit =>
            documents().Values.Any(open => open.View.GoalId == document.View.GoalId &&
                open.View.Path.Value.Equals(edit.Path.Value, StringComparison.Ordinal) &&
                !open.Editor.Text.Equals(edit.OriginalText.Value, StringComparison.Ordinal)));
        if (dirty is not null)
        {
            document.SetStatus(
                $"Save or revert unsaved changes in {dirty.Path.Value} before applying this refactoring.");
            return;
        }
        DocumentTransformationPreviewRequest request = new(
            document.View.GoalId.Value,
            new(document.View.Path.Value),
            new(document.View.Sha256.Value),
            version,
            new(document.Editor.Text),
            document.Editor.CaretPosition,
            kind,
            range,
            DocumentTransformationOrigin.Human,
            [],
            importNamespace,
            formattingTrigger,
            codeActionId,
            codeActionScope);
        document.SetStatus($"Applying Roslyn refactoring atomically to {preview.Edits.Count:N0} file(s)…");
        DocumentTransformationApplyView result =
            await mutations.ApplyDocumentTransformationAsync(new(
                request, NewEditCorrelation(), preview.Fingerprint!), token);
        if (result.ErrorCode is not null || result.Preview is null)
        {
            document.SetStatus(result.Error ?? "The multi-file refactoring was not applied.");
            return;
        }
        foreach (WorkbenchCodeDocumentTransformationEdit edit in result.Preview.Edits)
        {
            SourceDocumentSession? open = documents().Values.FirstOrDefault(candidate =>
                candidate.View.GoalId == document.View.GoalId &&
                candidate.View.Path.Value.Equals(edit.Path.Value, StringComparison.Ordinal));
            FileEditView? evidence = result.Files.FirstOrDefault(file =>
                file.Path.Equals(edit.Path.Value, StringComparison.Ordinal));
            if (open is null || evidence?.NewSha256 is null) continue;
            open.ReplaceWith(open.View with
            {
                Content = new(edit.Text.Value),
                Sha256 = new(evidence.NewSha256),
                Size = new(evidence.BytesWritten),
            });
            open.SetStatus("Applied compiler-verified atomic Roslyn refactoring.");
        }
        document.SetStatus($"Applied Roslyn refactoring atomically to {result.Files.Count:N0} file(s).");
        await invalidate();
        intelligence.ScheduleDiagnostics(document, immediate: true);
    }

    internal async ValueTask ShowQuickFixesAsync(SourceDocumentSession document)
    {
        if (!CanTransform(document, WorkbenchCodeDocumentTransformationKind.AddMissingImport))
        {
            document.SetStatus("Quick fixes require an editable C# source document.");
            return;
        }
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        document.SetBusy(true, "Finding Roslyn fixes at the caret…");
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null || !document.IsCurrentInteraction(version)) return;
            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                document, session, version);
            WorkbenchCodeRange? actionRange = document.Editor.SelectionRange;
            WorkbenchCodeMissingImportView imports = await code.GetMissingImportsAsync(snapshot, token);
            WorkbenchCodeActionView actions = await code.GetCodeActionsAsync(
                new(snapshot, actionRange), token);
            if (!document.IsCurrentInteraction(version))
            {
                document.SetStatus("The buffer changed before quick fixes were ready.");
                return;
            }
            if (imports.Candidates.Count == 0 && actions.Candidates.Count == 0)
            {
                document.SetStatus(actions.Issues.FirstOrDefault()?.Message.Value ??
                    imports.Issues.FirstOrDefault()?.Message.Value ??
                    "No supported quick fix is available at the caret.");
                return;
            }
            StackPanel choices = new() { Spacing = 4, Margin = new(4) };
            Flyout flyout = new() { Content = choices };
            foreach (WorkbenchCodeMissingImportCandidate candidate in imports.Candidates)
            {
                Button action = ActionButton(
                    $"using {candidate.Namespace.Value};  ·  {candidate.Symbol.Value}",
                    $"Add using {candidate.Namespace.Value} for {candidate.Symbol.Value}");
                action.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await TransformAsync(document,
                        WorkbenchCodeDocumentTransformationKind.AddMissingImport,
                        candidate.Namespace);
                };
                choices.Children.Add(action);
            }
            foreach (WorkbenchCodeActionCandidate candidate in actions.Candidates)
            {
                string suffix = candidate.Scope is WorkbenchCodeActionScope.Document
                    ? "  ·  Fix all in document"
                    : candidate.AffectedFileCount > 1 || !candidate.ChangesActiveDocument
                        ? $"  ·  {candidate.AffectedFileCount:N0} files · atomic"
                        : string.Empty;
                string name = candidate.Scope is WorkbenchCodeActionScope.Document
                    ? $"{candidate.Title.Value}, fix all in document"
                    : candidate.AffectedFileCount > 1 || !candidate.ChangesActiveDocument
                        ? $"{candidate.Title.Value}, affects {candidate.AffectedFileCount:N0} files, atomic apply"
                        : candidate.Title.Value;
                Button action = ActionButton(candidate.Title.Value + suffix, name);
                action.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await TransformAsync(document,
                        WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                        range: actionRange,
                        codeActionId: candidate.Id,
                        codeActionScope: candidate.Scope);
                };
                choices.Children.Add(action);
            }
            flyout.ShowAt(document.Surface.QuickFix);
            document.SetStatus(
                $"{imports.Candidates.Count + actions.Candidates.Count:N0} Roslyn quick fix(es) available.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            document.SetStatus("Quick-fix discovery cancelled.");
        }
        finally
        {
            document.SetBusy(false);
        }
    }

    private static Button ActionButton(string content, string name)
    {
        Button button = new() { Content = content, HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static string BusyText(
        WorkbenchCodeDocumentTransformationKind kind,
        WorkbenchCodeImportNamespace? importNamespace) => kind switch
    {
        WorkbenchCodeDocumentTransformationKind.FormatDocument => "Formatting the document with Roslyn…",
        WorkbenchCodeDocumentTransformationKind.FormatSelection => "Formatting the selected code with Roslyn…",
        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans => "Formatting changed code with Roslyn…",
        WorkbenchCodeDocumentTransformationKind.OrganizeImports => "Organizing imports with Roslyn…",
        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports => "Removing unused imports with Roslyn…",
        WorkbenchCodeDocumentTransformationKind.AddMissingImport => $"Adding {importNamespace?.Value} with Roslyn…",
        WorkbenchCodeDocumentTransformationKind.ApplyCodeAction => "Applying the selected Roslyn code action…",
        _ => "Preparing deterministic transformation…",
    };

    private static string NoChangeText(WorkbenchCodeDocumentTransformationKind kind) => kind switch
    {
        WorkbenchCodeDocumentTransformationKind.OrganizeImports => "Imports are already organized.",
        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports => "No compiler-proven unused imports were found.",
        WorkbenchCodeDocumentTransformationKind.AddMissingImport => "The selected import is already present.",
        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans => "Changed code is already formatted.",
        WorkbenchCodeDocumentTransformationKind.FormatPaste or
            WorkbenchCodeDocumentTransformationKind.FormatOnType => "No automatic formatting was needed.",
        WorkbenchCodeDocumentTransformationKind.ApplyCodeAction =>
            "The selected code action no longer changes this document.",
        _ => "The requested code is already formatted.",
    };

    private static string AppliedText(
        WorkbenchCodeDocumentTransformationKind kind,
        int count,
        WorkbenchCodeImportNamespace? importNamespace,
        WorkbenchCodeActionScope? scope) => kind switch
    {
        WorkbenchCodeDocumentTransformationKind.FormatDocument =>
            $"Formatted document · {count:N0} Roslyn edit(s) · undo available.",
        WorkbenchCodeDocumentTransformationKind.FormatSelection =>
            $"Formatted selection · {count:N0} Roslyn edit(s) · undo available.",
        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans =>
            $"Formatted changed code · {count:N0} Roslyn edit(s) · undo available.",
        WorkbenchCodeDocumentTransformationKind.FormatPaste =>
            "Formatted pasted code with Roslyn · undo available.",
        WorkbenchCodeDocumentTransformationKind.FormatOnType =>
            "Formatted current code with Roslyn · undo available.",
        WorkbenchCodeDocumentTransformationKind.OrganizeImports =>
            $"Organized imports · {count:N0} Roslyn edit(s) · undo available.",
        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports =>
            $"Removed unused imports · {count:N0} Roslyn edit(s) · undo available.",
        WorkbenchCodeDocumentTransformationKind.AddMissingImport =>
            $"Added using {importNamespace?.Value} · undo available.",
        WorkbenchCodeDocumentTransformationKind.ApplyCodeAction =>
            scope is WorkbenchCodeActionScope.Document
                ? $"Applied Roslyn fix to this document · {count:N0} edit(s) · undo available."
                : $"Applied Roslyn quick fix · {count:N0} edit(s) · undo available.",
        _ => "Applied deterministic Roslyn transformation to the live buffer.",
    };

    private static WorkbenchCodeFormattingTrigger? FormattingTrigger(char value) => value switch
    {
        ';' => WorkbenchCodeFormattingTrigger.Semicolon,
        '}' => WorkbenchCodeFormattingTrigger.CloseBrace,
        '\n' or '\r' => WorkbenchCodeFormattingTrigger.NewLine,
        _ => null,
    };

    private static int MapOffset(string original, string candidate, int offset)
    {
        int bounded = Math.Clamp(offset, 0, original.Length);
        int prefix = 0;
        int limit = Math.Min(original.Length, candidate.Length);
        while (prefix < limit && original[prefix] == candidate[prefix]) prefix++;
        if (bounded <= prefix) return bounded;
        int suffix = 0;
        while (suffix < original.Length - prefix && suffix < candidate.Length - prefix &&
               original[original.Length - suffix - 1] == candidate[candidate.Length - suffix - 1])
            suffix++;
        int originalEnd = original.Length - suffix;
        int candidateEnd = candidate.Length - suffix;
        if (bounded >= originalEnd)
            return Math.Clamp(candidateEnd + bounded - originalEnd, 0, candidate.Length);
        return Math.Clamp(prefix + Math.Min(bounded - prefix, candidateEnd - prefix),
            0, candidate.Length);
    }

    private static ToolCorrelationId NewEditCorrelation() =>
        new($"desktop-edit-{Guid.NewGuid():N}");
}
