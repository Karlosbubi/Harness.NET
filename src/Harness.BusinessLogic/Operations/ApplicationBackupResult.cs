namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationBackupResult(
    ApplicationBackupView? Backup,
    ApplicationBackupFailure? Failure,
    string? Error);
