namespace Harness.BusinessLogic.Mutations;

public sealed record DotNetOperationView(
    string GoalId,
    string CorrelationId,
    string Operation,
    string EntryPoint,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool IsOutputTruncated,
    bool IsErrorTruncated,
    bool WasCancelled,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Error);
