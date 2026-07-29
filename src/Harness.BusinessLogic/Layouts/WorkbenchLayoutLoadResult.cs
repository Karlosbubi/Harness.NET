namespace Harness.BusinessLogic.Layouts;

public sealed record WorkbenchLayoutLoadResult(
    WorkbenchLayoutLoadState State,
    WorkbenchLayoutPayload? Layout,
    string? Error);
