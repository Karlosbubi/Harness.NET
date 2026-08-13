namespace Harness.DataAccess.CodeIntelligence;

public interface ICodeIntelligenceEngine
{
    ValueTask<CodeIntelligenceSessionResult> OpenAsync(
        CodeIntelligenceOpenRequest request,
        IProgress<CodeIntelligenceLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceDiagnosticResult> GetDiagnosticsAsync(
        CodeIntelligenceDocumentSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceValidationResult> ValidateAsync(
        CodeIntelligenceValidationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceCompletionResult> GetCompletionsAsync(
        CodeIntelligenceCompletionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceCompletionCommitResult> CommitCompletionAsync(
        CodeIntelligenceCompletionCommitRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceQuickInfoResult> GetQuickInfoAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceSignatureHelpResult> GetSignatureHelpAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceNavigationResult> FindDefinitionAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceNavigationResult> FindReferencesAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<CodeIntelligenceNavigationResult> FindImplementationsAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CodeIntelligenceNavigationResult(
            snapshot.ContextId,
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            CodeIntelligenceResultState.Failed,
            [],
            [new(new("implementations_not_supported"),
                new("Implementation lookup is unavailable."))]));

    ValueTask<CodeIntelligenceVirtualDocumentResult> GetVirtualDocumentAsync(
        CodeIntelligenceVirtualDocumentRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new CodeIntelligenceVirtualDocumentResult(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                CodeIntelligenceResultState.Failed,
                request.Id,
                Kind: null,
                Title: null,
                Text: null,
                SelectionRange: null,
                Origin: null,
                IsReadOnly: true,
                [new(new("virtual_document_not_supported"),
                    new("Virtual source documents are unavailable."))]));

    ValueTask<CodeIntelligenceInspectionResult> InspectAsync(
        CodeIntelligenceInspectionRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new CodeIntelligenceInspectionResult(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                CodeIntelligenceResultState.Failed,
                request.Kind,
                Title: null,
                Text: null,
                Origin: null,
                IsReadOnly: true,
                IsTruncated: false,
                [new(new("inspection_not_supported"),
                    new("Code inspection views are unavailable."))]));

    ValueTask<CodeIntelligenceSemanticResult> SearchSymbolsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) => SemanticUnavailable(query, "symbol_search_not_supported");

    ValueTask<CodeIntelligenceSemanticResult> AnalyzeCallsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) => SemanticUnavailable(query, "call_analysis_not_supported");

    ValueTask<CodeIntelligenceSemanticResult> GetTypeHierarchyAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) => SemanticUnavailable(query, "type_hierarchy_not_supported");

    ValueTask<CodeIntelligenceSemanticResult> FindAssociatedTestsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) => SemanticUnavailable(query, "test_association_not_supported");

    ValueTask<CodeIntelligenceDocumentPresentationResult> GetDocumentPresentationAsync(
        CodeIntelligenceDocumentPresentationRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new CodeIntelligenceDocumentPresentationResult(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                CodeIntelligenceResultState.Failed,
                [], [], [], [], [], [], false,
                [new(new("document_presentation_not_supported"),
                    new("Semantic document presentation is unavailable."))]));

    ValueTask<CodeIntelligenceOccurrenceResult> FindOccurrencesAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new CodeIntelligenceOccurrenceResult(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                CodeIntelligenceResultState.Failed,
                null,
                [],
                false,
                [new(new("occurrences_not_supported"),
                    new("Semantic occurrence lookup is unavailable."))]));

    ValueTask<CodeIntelligenceRenamePreviewResult> PreviewRenameAsync(
        CodeIntelligenceRenamePreviewRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CodeIntelligenceRenamePreviewResult(
            request.Snapshot.ContextId,
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            CodeIntelligenceResultState.Failed,
            CodeIntelligenceTransformationDisposition.Rejected,
            Symbol: null,
            request.NewName,
            [],
            [],
            [],
            Fingerprint: null,
            [new(new("rename_not_supported"), new("Semantic rename is unavailable."))]));

    ValueTask<CodeIntelligenceDocumentTransformationPreviewResult> PreviewDocumentTransformationAsync(
        CodeIntelligenceDocumentTransformationPreviewRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CodeIntelligenceDocumentTransformationPreviewResult(
            request.Snapshot.ContextId,
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            CodeIntelligenceResultState.Failed,
            CodeIntelligenceTransformationDisposition.Rejected,
            request.Kind,
            request.Range,
            Edit: null,
            [],
            [],
            Fingerprint: null,
            [new(new("document_transformation_not_supported"),
                new("Document formatting and import organization are unavailable."))],
            request.ImportNamespace));

    ValueTask<CodeIntelligenceMissingImportResult> GetMissingImportsAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CodeIntelligenceMissingImportResult(
            snapshot.ContextId,
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            CodeIntelligenceResultState.Failed,
            [],
            [new(new("missing_imports_not_supported"),
                new("Missing-import discovery is unavailable."))]));

    ValueTask<CodeIntelligenceCodeActionResult> GetCodeActionsAsync(
        CodeIntelligenceCodeActionRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CodeIntelligenceCodeActionResult(
            request.Snapshot.ContextId,
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            CodeIntelligenceResultState.Failed,
            [],
            [new(new("code_actions_not_supported"),
                new("Contextual code actions are unavailable."))]));

    ValueTask CloseAsync(
        CodeIntelligenceSessionId sessionId,
        CancellationToken cancellationToken = default);

    private static ValueTask<CodeIntelligenceSemanticResult> SemanticUnavailable(
        CodeIntelligenceSemanticQuery query, string code) => ValueTask.FromResult<CodeIntelligenceSemanticResult>(new(
            query.Snapshot.ContextId, query.Snapshot.SessionId, query.Snapshot.Path,
            query.Snapshot.BufferVersion, CodeIntelligenceResultState.Failed, [], null, false,
            [new(new(code), new("The requested semantic graph is unavailable."))]));
}
