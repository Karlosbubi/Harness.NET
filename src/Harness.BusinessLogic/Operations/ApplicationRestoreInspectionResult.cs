namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationRestoreInspectionResult(
    ApplicationRestoreView? Restore,
    ApplicationRestoreFailure? Failure,
    string? Error);
