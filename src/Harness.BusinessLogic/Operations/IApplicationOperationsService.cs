namespace Harness.BusinessLogic.Operations;

public interface IApplicationOperationsService
{
    ValueTask<ApplicationBackupResult> CreateBackupAsync(
        BackupDestinationPath destination,
        CancellationToken cancellationToken = default);
}
