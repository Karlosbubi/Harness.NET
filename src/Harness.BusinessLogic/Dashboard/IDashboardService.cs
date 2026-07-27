namespace Harness.BusinessLogic.Dashboard;

public interface IDashboardService
{
    ValueTask<DashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<DashboardSnapshot> SubmitAsync(
        string instruction,
        CancellationToken cancellationToken = default);

    ValueTask<DashboardSnapshot> RefreshProviderAsync(
        CancellationToken cancellationToken = default);

    ValueTask<DashboardSnapshot> SelectModelAsync(
        string model,
        CancellationToken cancellationToken = default);
}
