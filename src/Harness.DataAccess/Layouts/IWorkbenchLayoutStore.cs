namespace Harness.DataAccess.Layouts;

public interface IWorkbenchLayoutStore
{
    ValueTask<WorkbenchLayoutStoreReadResult> ReadAsync(
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchLayoutStoreWriteResult> WriteAsync(
        WorkbenchLayoutContent layout,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchLayoutStoreWriteResult> ResetAsync(
        CancellationToken cancellationToken = default);
}
