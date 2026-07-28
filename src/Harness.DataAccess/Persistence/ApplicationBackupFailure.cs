namespace Harness.DataAccess.Persistence;

public enum ApplicationBackupFailure
{
    InvalidDestination,
    DatabaseMissing,
    IntegrityCheckFailed,
    ArchiveCreationFailed,
}
