using Harness.BusinessLogic.Operations;
using Harness.DataAccess.Persistence;
using StoredBackupResult = Harness.DataAccess.Persistence.ApplicationBackupResult;

namespace Harness.BusinessLogic.Tests.Operations;

public sealed class ApplicationOperationsServiceTests
{
    [Fact]
    public async Task Maps_verified_backup_evidence_to_presentation_contract()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        StubBackup backup = new(new(
            new("/tmp/export.zip"),
            new(new string('a', 64)),
            new(new string('b', 64)),
            new(123),
            new(new string('c', 64)),
            new(456),
            new(17),
            now,
            Failure: null,
            Error: null));
        ApplicationOperationsService service = new(backup, new StubRestore());

        Harness.BusinessLogic.Operations.ApplicationBackupResult result =
            await service.CreateBackupAsync(new("/tmp/export.zip"));

        Assert.Null(result.Error);
        Assert.Equal("/tmp/export.zip", result.Backup?.Archive.Value);
        Assert.Equal(17, result.Backup?.SchemaVersion.Value);
        Assert.Equal(new string('a', 64), result.Backup?.ArchiveSha256.Value);
        Assert.Equal(new string('c', 64), result.Backup?.WorkbenchLayoutSha256?.Value);
    }

    [Fact]
    public async Task Maps_verified_restore_and_restart_boundary()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        StubRestore restore = new(new(new(
            new("/tmp/restore.zip"), new(new string('a', 64)),
            new(new string('b', 64)), new(123), null, null, new(21), now,
            ApplicationBackupFormat.Version2), true, null, null));
        ApplicationOperationsService service = new(
            new StubBackup(new(null, null, null, null, null, null, null, null,
                Harness.DataAccess.Persistence.ApplicationBackupFailure.DatabaseMissing,
                "unused")), restore);

        Harness.BusinessLogic.Operations.ApplicationRestoreStageResult result =
            await service.StageRestoreAsync(new(
                new("/tmp/restore.zip"), new(new string('a', 64))));

        Assert.True(result.RestartRequired);
        Assert.Equal(21, result.Restore?.SchemaVersion.Value);
        Assert.Equal(new string('a', 64), result.Restore?.ArchiveSha256.Value);
    }

    private sealed class StubBackup(StoredBackupResult result) : IApplicationBackup
    {
        public ValueTask<StoredBackupResult> CreateAsync(
            ApplicationBackupRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }

    private sealed class StubRestore(
        Harness.DataAccess.Persistence.ApplicationRestoreStageResult? staged = null)
        : IApplicationRestore
    {
        public ValueTask<Harness.DataAccess.Persistence.ApplicationRestoreInspectionResult> InspectAsync(
            BackupArchivePath source,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new Harness.DataAccess.Persistence.ApplicationRestoreInspectionResult(
                    staged?.Archive, staged?.Failure, staged?.Error));

        public ValueTask<Harness.DataAccess.Persistence.ApplicationRestoreStageResult> StageAsync(
            BackupArchivePath source,
            BackupSha256 expectedArchiveSha256,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(staged ??
                new Harness.DataAccess.Persistence.ApplicationRestoreStageResult(
                    null, false,
                    Harness.DataAccess.Persistence.ApplicationRestoreFailure.InvalidSource,
                    "unused"));

        public ValueTask<ApplicationRestoreApplyResult> ApplyPendingAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new ApplicationRestoreApplyResult(false, false, null, null, null, null));
    }
}
