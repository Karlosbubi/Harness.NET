namespace Harness.BusinessLogic.Mutations;

public interface IWorkspaceMutationService
{
    ValueTask<FileEditView> ApplyFileEditAsync(
        FileEditRequest request,
        CancellationToken cancellationToken = default);
}
