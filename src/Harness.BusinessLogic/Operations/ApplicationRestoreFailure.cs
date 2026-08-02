namespace Harness.BusinessLogic.Operations;

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
