using DataLayouts = Harness.DataAccess.Layouts;

namespace Harness.BusinessLogic.Layouts;

internal sealed class WorkbenchLayoutService(DataLayouts.IWorkbenchLayoutStore store)
    : IWorkbenchLayoutService
{
    public async ValueTask<WorkbenchLayoutLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        DataLayouts.WorkbenchLayoutStoreReadResult result =
            await store.ReadAsync(cancellationToken);
        if (result.Layout is not null)
        {
            return new(
                WorkbenchLayoutLoadState.Available,
                new(result.Layout.Value),
                null);
        }

        return result.Failure is null
            ? new(WorkbenchLayoutLoadState.Missing, null, null)
            : new(WorkbenchLayoutLoadState.Rejected, null, result.Error);
    }

    public async ValueTask<WorkbenchLayoutWriteResult> SaveAsync(
        WorkbenchLayoutPayload layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        DataLayouts.WorkbenchLayoutStoreWriteResult result = await store.WriteAsync(
            new(layout.Value),
            cancellationToken);
        return new(result.Succeeded, result.Error);
    }

    public async ValueTask<WorkbenchLayoutWriteResult> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        DataLayouts.WorkbenchLayoutStoreWriteResult result =
            await store.ResetAsync(cancellationToken);
        return new(result.Succeeded, result.Error);
    }
}
