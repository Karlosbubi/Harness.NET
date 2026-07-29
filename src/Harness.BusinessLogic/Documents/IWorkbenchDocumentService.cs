namespace Harness.BusinessLogic.Documents;

public interface IWorkbenchDocumentService
{
    ValueTask<WorkbenchDocumentView> OpenAsync(
        WorkbenchDocumentOpenRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchDocumentSaveResult> SaveAsync(
        WorkbenchDocumentSaveRequest request,
        CancellationToken cancellationToken = default);
}
