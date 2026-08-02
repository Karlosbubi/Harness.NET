namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationRestoreStageResult(
    ApplicationRestoreView? Restore,
    bool RestartRequired,
    ApplicationRestoreFailure? Failure,
    string? Error);
