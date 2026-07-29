namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationBackupView(
    BackupDestinationPath Archive,
    BackupHash ArchiveSha256,
    BackupHash DatabaseSha256,
    BackupSize DatabaseBytes,
    BackupHash? WorkbenchLayoutSha256,
    BackupSize? WorkbenchLayoutBytes,
    ApplicationSchemaVersion SchemaVersion,
    DateTimeOffset CreatedAt);
