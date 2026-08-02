namespace Harness.DataAccess.Persistence;

public enum ApplicationRestoreFailure
{
    InvalidSource,
    UnsupportedArchive,
    IntegrityMismatch,
    DatabaseInvalid,
    SchemaTooNew,
    PendingRestoreExists,
    StagingFailed,
    ApplyFailed,
}
