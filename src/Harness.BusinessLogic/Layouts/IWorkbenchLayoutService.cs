namespace Harness.BusinessLogic.Layouts;

public interface IWorkbenchLayoutService
{
    ValueTask<WorkbenchLayoutLoadResult> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchLayoutWriteResult> SaveAsync(
        WorkbenchLayoutPayload layout,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchLayoutWriteResult> ResetAsync(
        CancellationToken cancellationToken = default);
}
