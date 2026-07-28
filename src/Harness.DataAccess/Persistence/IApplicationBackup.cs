namespace Harness.DataAccess.Persistence;

public interface IApplicationBackup
{
    ValueTask<ApplicationBackupResult> CreateAsync(
        ApplicationBackupRequest request,
        CancellationToken cancellationToken = default);
}
