using System.Collections.Immutable;

namespace Harness.DataAccess.Execution;

public sealed record DotNetProjectPath(string Value);
public sealed record DotNetTargetFramework(string Value);
public sealed record DotNetConfigurationName(string Value);
public sealed record DotNetTestFullyQualifiedName(string Value);
public sealed record DotNetExecutionOutput(string Value);
public sealed record DotNetTestDisplayName(string Value);

public enum DotNetTestOutcome
{
    Passed,
    Failed,
    Skipped,
    Other,
}

public sealed record DotNetTestCaseResult(
    DotNetTestFullyQualifiedName FullyQualifiedName,
    DotNetTestDisplayName DisplayName,
    DotNetTestOutcome Outcome,
    long DurationMilliseconds);

public enum DotNetProjectOperation
{
    Run,
    Build,
    Rebuild,
    Test,
}

public enum DotNetTestScope
{
    Exact,
    Type,
    Project,
    Selection,
}

public sealed record DotNetProjectExecutionRequest(
    DotNetProjectPath ProjectPath,
    DotNetTargetFramework? TargetFramework,
    DotNetProjectOperation Operation = DotNetProjectOperation.Run,
    DotNetConfigurationName? Configuration = null,
    DotNetTestFullyQualifiedName? Test = null,
    DotNetTestScope TestScope = DotNetTestScope.Exact,
    ImmutableArray<DotNetTestFullyQualifiedName> SelectedTests = default);

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
    string? Error,
    ImmutableArray<DotNetTestCaseResult> TestCases = default,
    bool AreTestCasesTruncated = false);

public interface IDotNetProjectRunner
{
    ValueTask<DotNetProjectExecutionResult> RunAsync(
        string sourceRoot,
        DotNetProjectExecutionRequest request,
        CancellationToken cancellationToken = default);
}
