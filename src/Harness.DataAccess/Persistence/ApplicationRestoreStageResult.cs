namespace Harness.DataAccess.Persistence;

public sealed record ApplicationRestoreStageResult(
    ApplicationRestoreArchive? Archive,
    bool RestartRequired,
    ApplicationRestoreFailure? Failure,
    string? Error);
