namespace Harness.DataAccess.Layouts;

public sealed record WorkbenchLayoutStoreReadResult(
    WorkbenchLayoutContent? Layout,
    WorkbenchLayoutStoreFailure? Failure,
    string? Error);
