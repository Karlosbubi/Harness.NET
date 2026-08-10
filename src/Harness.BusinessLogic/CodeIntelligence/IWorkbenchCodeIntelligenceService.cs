namespace Harness.BusinessLogic.CodeIntelligence;

public interface IWorkbenchCodeIntelligenceService
{
    ValueTask<WorkbenchCodeSessionView> StartAsync(
        WorkbenchCodeSessionRequest request,
        IProgress<WorkbenchCodeLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
        WorkbenchCodeDocumentSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeValidationView> ValidateAsync(
        WorkbenchCodeValidationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeCompletionView> GetCompletionsAsync(
        WorkbenchCodeCompletionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeCompletionCommitView> CommitCompletionAsync(
        WorkbenchCodeCompletionCommitRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeQuickInfoView> GetQuickInfoAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeSignatureHelpView> GetSignatureHelpAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeNavigationView> FindDefinitionAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeNavigationView> FindReferencesAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeNavigationView> FindImplementationsAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new WorkbenchCodeNavigationView(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            WorkbenchCodeResultState.Failed,
            [],
            [new(new("implementations_not_supported"),
                new("Implementation lookup is unavailable."))]));

    ValueTask<WorkbenchCodeRenamePreviewView> PreviewRenameAsync(
        WorkbenchCodeRenamePreviewRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new WorkbenchCodeRenamePreviewView(
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            WorkbenchCodeResultState.Failed,
            WorkbenchCodeTransformationDisposition.Rejected,
            Symbol: null,
            request.NewName,
            [],
            [],
            [],
            Fingerprint: null,
            [new(new("rename_not_supported"), new("Semantic rename is unavailable."))]));

    ValueTask StopAsync(
        WorkbenchCodeSessionId sessionId,
        CancellationToken cancellationToken = default);
}
