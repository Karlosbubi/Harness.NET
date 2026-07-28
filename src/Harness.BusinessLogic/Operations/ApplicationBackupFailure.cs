namespace Harness.BusinessLogic.Operations;

public enum ApplicationBackupFailure
{
    InvalidDestination,
    DatabaseMissing,
    IntegrityCheckFailed,
    ArchiveCreationFailed,
}
