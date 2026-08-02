namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationRestoreView(
    RestoreSourcePath Archive,
    BackupHash ArchiveSha256,
    BackupHash DatabaseSha256,
    BackupSize DatabaseBytes,
    BackupHash? WorkbenchLayoutSha256,
    BackupSize? WorkbenchLayoutBytes,
    ApplicationSchemaVersion SchemaVersion,
    DateTimeOffset CreatedAt,
    RestoreArchiveFormat Format);
