namespace Harness.BusinessLogic.Evidence;

public sealed record RunOutputSnapshot(
    IReadOnlyList<RunOutputView> Items,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
