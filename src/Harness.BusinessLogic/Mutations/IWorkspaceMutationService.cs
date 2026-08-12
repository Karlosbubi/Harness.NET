namespace Harness.BusinessLogic.Mutations;

public interface IWorkspaceMutationService
{
    ValueTask<FileEditView> ApplyFileEditAsync(
        FileEditRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<RenameSymbolPreviewView> PreviewRenameAsync(
        RenameSymbolPreviewRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RenameSymbolPreviewView(
            Preview: null,
            "rename_not_supported",
            "Semantic rename is unavailable."));

    ValueTask<RenameSymbolApplyView> ApplyRenameAsync(
        RenameSymbolApplyRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RenameSymbolApplyView(
            request.PreviewRequest.GoalId,
            request.CorrelationId,
            Preview: null,
            [],
            WasRolledBack: false,
            WasCancelled: false,
            AppliedCodeValidation: null,
            "rename_not_supported",
            "Semantic rename is unavailable."));

    ValueTask<DocumentTransformationPreviewView> PreviewDocumentTransformationAsync(
        DocumentTransformationPreviewRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new DocumentTransformationPreviewView(
            Preview: null,
            "document_transformation_not_supported",
            "Document formatting and import organization are unavailable."));

    ValueTask<DocumentTransformationApplyView> ApplyDocumentTransformationAsync(
        DocumentTransformationApplyRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new DocumentTransformationApplyView(
            request.PreviewRequest.GoalId,
            request.CorrelationId,
            Preview: null,
            [],
            WasRolledBack: false,
            WasCancelled: false,
            AppliedCodeValidation: null,
            "document_transformation_not_supported",
            "Document formatting and import organization are unavailable."));

    ValueTask<DotNetOperationView> RunDotNetAsync(
        DotNetOperationRequest request,
        CancellationToken cancellationToken = default);
}
