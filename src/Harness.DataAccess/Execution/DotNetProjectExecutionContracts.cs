namespace Harness.DataAccess.Execution;

public sealed record DotNetProjectPath(string Value);
public sealed record DotNetTargetFramework(string Value);
public sealed record DotNetConfigurationName(string Value);
public sealed record DotNetExecutionOutput(string Value);

public enum DotNetProjectOperation
{
    Run,
    Build,
    Rebuild,
}

public sealed record DotNetProjectExecutionRequest(
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    DotNetProjectOperation Operation = DotNetProjectOperation.Run,
    DotNetConfigurationName? Configuration = null);

public sealed record DotNetProjectExecutionResult(
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    int? ExitCode,
    DotNetExecutionOutput StandardOutput,
    DotNetExecutionOutput StandardError,
    bool IsOutputTruncated,
    bool IsErrorTruncated,
    bool WasCancelled,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Error);

public interface IDotNetProjectRunner
{
    ValueTask<DotNetProjectExecutionResult> RunAsync(
        string sourceRoot,
        DotNetProjectExecutionRequest request,
        CancellationToken cancellationToken = default);
}
