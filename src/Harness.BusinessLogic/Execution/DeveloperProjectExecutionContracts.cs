using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Execution;

public sealed record DeveloperExecutionId(string Value);
public sealed record DeveloperExecutionOutput(string Value);
public sealed record DeveloperProjectPath(string Value);
public sealed record DeveloperTargetFramework(string Value);
public sealed record DeveloperConfigurationName(string Value);
public sealed record DeveloperTestId(string Value);
public sealed record DeveloperTestName(string Value);
public sealed record DeveloperTestDisplayName(string Value);

public enum DeveloperTestOutcome
{
    Passed,
    Failed,
    Skipped,
    Other,
}

public sealed record DeveloperTestCaseResult(
    DeveloperTestName FullyQualifiedName,
    DeveloperTestDisplayName DisplayName,
    DeveloperTestOutcome Outcome,
    long DurationMilliseconds);

public enum DeveloperExecutionOperation
{
    Run,
    Build,
    Rebuild,
    Test,
}

public sealed record DeveloperProjectTarget(
    DeveloperProjectPath ProjectPath,
    DeveloperTargetFramework? TargetFramework,
    DeveloperConfigurationName? Configuration);

public enum DeveloperTestScope
{
    Exact,
    Type,
    Project,
    Selection,
}

public sealed record DeveloperTestTarget(
    DeveloperTestId Id,
    DeveloperTestName FullyQualifiedName,
    DeveloperTestScope Scope = DeveloperTestScope.Exact,
    ImmutableArray<DeveloperTestName> SelectedTests = default)
{
    public static DeveloperTestTarget ForType(
        DeveloperProjectPath project,
        DeveloperTestName type) => Group(DeveloperTestScope.Type, project, type);

    public static DeveloperTestTarget ForProject(DeveloperProjectPath project) =>
        Group(DeveloperTestScope.Project, project, new(project.Value));

    public static DeveloperTestTarget ForSelection(
        DeveloperProjectPath project,
        IEnumerable<DeveloperTestName> tests)
    {
        ImmutableArray<DeveloperTestName> selected = tests
            .DistinctBy(test => test.Value, StringComparer.Ordinal)
            .OrderBy(test => test.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        string input = $"{DeveloperTestScope.Selection}\n{project.Value}\n" +
                       string.Join('\n', selected.Select(test => test.Value));
        return new(
            new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))),
            new($"{selected.Length} selected tests"),
            DeveloperTestScope.Selection,
            selected);
    }

    private static DeveloperTestTarget Group(
        DeveloperTestScope scope,
        DeveloperProjectPath project,
        DeveloperTestName name)
    {
        string input = $"{scope}\n{project.Value}\n{name.Value}";
        return new(
            new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))),
            name,
            scope);
    }
}

public enum DeveloperExecutionState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

public sealed record DeveloperExecutionCapabilities(
    bool CanRunProjectEntryPoint,
    bool CanBuildProject,
    bool CanRebuildProject,
    bool CanDebugProjectEntryPoint,
    string DebugStatus,
    bool CanTest = false);

public sealed record DeveloperExecutionView(
    DeveloperExecutionId Id,
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    string SourceDescription,
    DeveloperExecutionOperation Operation,
    DeveloperProjectTarget Project,
    WorkbenchExecutionTarget? EntryPoint,
    DeveloperExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    long DurationMilliseconds,
    DeveloperExecutionOutput? StandardOutput,
    DeveloperExecutionOutput? StandardError,
    bool IsOutputTruncated,
    bool IsErrorTruncated,
    bool IsOutputAvailable,
    string? ErrorCode,
    string? Error,
    DeveloperTestTarget? Test = null,
    ImmutableArray<DeveloperTestCaseResult> TestCases = default,
    bool AreTestCasesTruncated = false);

public sealed record DeveloperExecutionStartRequest(
    WorkbenchWorkspaceRequest Workspace,
    WorkbenchExecutionTarget Target);

public sealed record DeveloperExecutionStartResult(
    DeveloperExecutionView? Execution,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperBuildStartRequest(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperExecutionOperation Operation,
    DeveloperProjectTarget Project);

public sealed record DeveloperTestStartRequest(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperProjectTarget Project,
    DeveloperTestTarget Test);

public sealed record DeveloperExecutionListResult(
    IReadOnlyList<DeveloperExecutionView> Executions,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record DeveloperExecutionCancelResult(
    bool CancellationRequested,
    string? ErrorCode,
    string? Error);

public interface IDeveloperProjectExecutionService
{
    DeveloperExecutionCapabilities Capabilities { get; }

    ValueTask<DeveloperExecutionStartResult> StartRunAsync(
        DeveloperExecutionStartRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperExecutionStartResult> StartBuildAsync(
        DeveloperBuildStartRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperExecutionStartResult> StartTestAsync(
        DeveloperTestStartRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new DeveloperExecutionStartResult(
                null,
                "test_execution_not_supported",
                "Developer test execution is unavailable."));

    ValueTask<DeveloperExecutionListResult> ListAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperExecutionCancelResult> CancelAsync(
        DeveloperExecutionId executionId,
        CancellationToken cancellationToken = default);
}
