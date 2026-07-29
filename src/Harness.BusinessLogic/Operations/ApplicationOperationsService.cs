using Harness.DataAccess.Persistence;
using StoredBackupResult = Harness.DataAccess.Persistence.ApplicationBackupResult;

namespace Harness.BusinessLogic.Operations;

internal sealed class ApplicationOperationsService(IApplicationBackup backup)
    : IApplicationOperationsService
{
    public async ValueTask<ApplicationBackupResult> CreateBackupAsync(
        BackupDestinationPath destination,
        CancellationToken cancellationToken = default)
    {
        if (destination is null || string.IsNullOrWhiteSpace(destination.Value))
        {
            return new(null, ApplicationBackupFailure.InvalidDestination,
                "A backup destination is required.");
        }

        StoredBackupResult result = await backup.CreateAsync(
            new(new(destination.Value)), cancellationToken);
        if (result.Archive is null)
        {
            return new(null, Map(result.Failure), result.Error);
        }

        return new(new(
            new(result.Archive.Value),
            new(result.ArchiveSha256!.Value),
            new(result.DatabaseSha256!.Value),
            new(result.DatabaseBytes!.Value),
            result.WorkbenchLayoutSha256 is null
                ? null
                : new(result.WorkbenchLayoutSha256.Value),
            result.WorkbenchLayoutBytes is null
                ? null
                : new(result.WorkbenchLayoutBytes.Value),
            new(result.SchemaVersion!.Value),
            result.CreatedAt!.Value), Failure: null, Error: null);
    }

    private static ApplicationBackupFailure? Map(
        Harness.DataAccess.Persistence.ApplicationBackupFailure? failure) => failure switch
    {
        null => null,
        Harness.DataAccess.Persistence.ApplicationBackupFailure.InvalidDestination =>
            ApplicationBackupFailure.InvalidDestination,
        Harness.DataAccess.Persistence.ApplicationBackupFailure.DatabaseMissing =>
            ApplicationBackupFailure.DatabaseMissing,
        Harness.DataAccess.Persistence.ApplicationBackupFailure.IntegrityCheckFailed =>
            ApplicationBackupFailure.IntegrityCheckFailed,
        Harness.DataAccess.Persistence.ApplicationBackupFailure.ArchiveCreationFailed =>
            ApplicationBackupFailure.ArchiveCreationFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
