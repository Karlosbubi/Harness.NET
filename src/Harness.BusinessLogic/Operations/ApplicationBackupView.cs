namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationBackupView(
    BackupDestinationPath Archive,
    BackupHash ArchiveSha256,
    BackupHash DatabaseSha256,
    BackupSize DatabaseBytes,
    ApplicationSchemaVersion SchemaVersion,
    DateTimeOffset CreatedAt);
