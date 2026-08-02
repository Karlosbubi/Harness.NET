namespace Harness.BusinessLogic.Operations;

public sealed record ApplicationRestoreStageRequest(
    RestoreSourcePath Source,
    BackupHash ExpectedArchiveSha256);
