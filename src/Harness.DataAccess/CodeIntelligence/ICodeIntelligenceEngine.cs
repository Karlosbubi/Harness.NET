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

    ValueTask CloseAsync(
        CodeIntelligenceSessionId sessionId,
        CancellationToken cancellationToken = default);

    private static ValueTask<CodeIntelligenceSemanticResult> SemanticUnavailable(
        CodeIntelligenceSemanticQuery query, string code) => ValueTask.FromResult<CodeIntelligenceSemanticResult>(new(
            query.Snapshot.ContextId, query.Snapshot.SessionId, query.Snapshot.Path,
            query.Snapshot.BufferVersion, CodeIntelligenceResultState.Failed, [], null, false,
            [new(new(code), new("The requested semantic graph is unavailable."))]));
}
