using Harness.DataAccess.Persistence;
using StoredBackupResult = Harness.DataAccess.Persistence.ApplicationBackupResult;

namespace Harness.BusinessLogic.Operations;

internal sealed class ApplicationOperationsService(
    IApplicationBackup backup,
    IApplicationRestore restore)
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

    public async ValueTask<ApplicationRestoreInspectionResult> InspectRestoreAsync(
        RestoreSourcePath source,
        CancellationToken cancellationToken = default)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Value))
        {
            return new(null, ApplicationRestoreFailure.InvalidSource,
                "A restore archive path is required.");
        }

        Harness.DataAccess.Persistence.ApplicationRestoreInspectionResult result =
            await restore.InspectAsync(new(source.Value), cancellationToken);
        return new(Map(result.Archive), Map(result.Failure), result.Error);
    }

    public async ValueTask<ApplicationRestoreStageResult> StageRestoreAsync(
        ApplicationRestoreStageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Source?.Value) ||
            string.IsNullOrWhiteSpace(request.ExpectedArchiveSha256?.Value))
        {
            return new(null, false, ApplicationRestoreFailure.InvalidSource,
                "A restore archive path is required.");
        }

        Harness.DataAccess.Persistence.ApplicationRestoreStageResult result =
            await restore.StageAsync(
                new(request.Source.Value),
                new(request.ExpectedArchiveSha256.Value),
                cancellationToken);
        return new(Map(result.Archive), result.RestartRequired,
            Map(result.Failure), result.Error);
    }

    private static ApplicationRestoreView? Map(ApplicationRestoreArchive? archive) =>
        archive is null ? null : new(
            new(archive.Archive.Value),
            new(archive.ArchiveSha256.Value),
            new(archive.DatabaseSha256.Value),
            new(archive.DatabaseBytes.Value),
            archive.WorkbenchLayoutSha256 is null
                ? null
                : new(archive.WorkbenchLayoutSha256.Value),
            archive.WorkbenchLayoutBytes is null
                ? null
                : new(archive.WorkbenchLayoutBytes.Value),
            new(archive.SchemaVersion.Value),
            archive.CreatedAt,
            archive.Format switch
            {
                ApplicationBackupFormat.Version1 => RestoreArchiveFormat.Version1,
                ApplicationBackupFormat.Version2 => RestoreArchiveFormat.Version2,
                _ => throw new ArgumentOutOfRangeException(nameof(archive)),
            });

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

    private static ApplicationRestoreFailure? Map(
        Harness.DataAccess.Persistence.ApplicationRestoreFailure? failure) => failure switch
    {
        null => null,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.InvalidSource =>
            ApplicationRestoreFailure.InvalidSource,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.UnsupportedArchive =>
            ApplicationRestoreFailure.UnsupportedArchive,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.IntegrityMismatch =>
            ApplicationRestoreFailure.IntegrityMismatch,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.DatabaseInvalid =>
            ApplicationRestoreFailure.DatabaseInvalid,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.SchemaTooNew =>
            ApplicationRestoreFailure.SchemaTooNew,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.PendingRestoreExists =>
            ApplicationRestoreFailure.PendingRestoreExists,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.StagingFailed =>
            ApplicationRestoreFailure.StagingFailed,
        Harness.DataAccess.Persistence.ApplicationRestoreFailure.ApplyFailed =>
            ApplicationRestoreFailure.ApplyFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
