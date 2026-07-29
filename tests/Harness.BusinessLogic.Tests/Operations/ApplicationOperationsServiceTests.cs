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
        ApplicationOperationsService service = new(backup);

        Harness.BusinessLogic.Operations.ApplicationBackupResult result =
            await service.CreateBackupAsync(new("/tmp/export.zip"));

        Assert.Null(result.Error);
        Assert.Equal("/tmp/export.zip", result.Backup?.Archive.Value);
        Assert.Equal(17, result.Backup?.SchemaVersion.Value);
        Assert.Equal(new string('a', 64), result.Backup?.ArchiveSha256.Value);
        Assert.Equal(new string('c', 64), result.Backup?.WorkbenchLayoutSha256?.Value);
    }

    private sealed class StubBackup(StoredBackupResult result) : IApplicationBackup
    {
        public ValueTask<StoredBackupResult> CreateAsync(
            ApplicationBackupRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }
}
