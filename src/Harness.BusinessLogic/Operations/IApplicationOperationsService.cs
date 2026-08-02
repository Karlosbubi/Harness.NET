namespace Harness.BusinessLogic.Operations;

public interface IApplicationOperationsService
{
    ValueTask<ApplicationBackupResult> CreateBackupAsync(
        BackupDestinationPath destination,
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRestoreInspectionResult> InspectRestoreAsync(
        RestoreSourcePath source,
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRestoreStageResult> StageRestoreAsync(
        ApplicationRestoreStageRequest request,
        CancellationToken cancellationToken = default);
}
