namespace Harness.DataAccess.Persistence;

public sealed record ApplicationRestoreApplyResult(
    bool HadPendingRestore,
    bool Applied,
    ApplicationSchemaVersion? RestoredSchemaVersion,
    string? RollbackDirectory,
    ApplicationRestoreFailure? Failure,
    string? Error);
