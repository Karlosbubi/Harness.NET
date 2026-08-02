namespace Harness.DataAccess.Persistence;

public interface IApplicationRestore
{
    ValueTask<ApplicationRestoreInspectionResult> InspectAsync(
        BackupArchivePath source,
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRestoreStageResult> StageAsync(
        BackupArchivePath source,
        BackupSha256 expectedArchiveSha256,
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRestoreApplyResult> ApplyPendingAsync(
        CancellationToken cancellationToken = default);
}
