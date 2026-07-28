namespace Harness.DataAccess.Persistence;

public sealed record DatabaseInitializationResult(
    ApplicationDatabasePath DatabasePath,
    ApplicationSchemaVersion SchemaVersion,
    DatabaseInitializationKind Kind,
    BackupArchivePath? PreUpgradeBackup);
