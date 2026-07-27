namespace Harness.DataAccess.Execution;

public sealed record DotNetToolResult(
    DotNetToolOperation Operation,
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
