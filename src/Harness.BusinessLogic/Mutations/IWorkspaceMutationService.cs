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

    ValueTask<DotNetOperationView> RunDotNetAsync(
        DotNetOperationRequest request,
        CancellationToken cancellationToken = default);
}
