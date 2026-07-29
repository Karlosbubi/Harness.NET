namespace Harness.DataAccess.Layouts;

public sealed record WorkbenchLayoutStoreWriteResult(
    bool Succeeded,
    WorkbenchLayoutStoreFailure? Failure,
    string? Error);
