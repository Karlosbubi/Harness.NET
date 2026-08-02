namespace Harness.DataAccess.Persistence;

public sealed record ApplicationRestoreInspectionResult(
    ApplicationRestoreArchive? Archive,
    ApplicationRestoreFailure? Failure,
    string? Error);
