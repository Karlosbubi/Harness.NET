namespace Harness.DataAccess.Persistence;

public sealed record ApplicationBackupResult(
    BackupArchivePath? Archive,
    BackupSha256? ArchiveSha256,
    BackupSha256? DatabaseSha256,
    BackupByteCount? DatabaseBytes,
    ApplicationSchemaVersion? SchemaVersion,
    DateTimeOffset? CreatedAt,
    ApplicationBackupFailure? Failure,
    string? Error);
