using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public sealed record DotNetOperationView(
    string GoalId,
    ToolCorrelationId CorrelationId,
    DotNetOperation Operation,
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
