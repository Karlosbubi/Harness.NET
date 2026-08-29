using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Execution;

internal sealed record DeveloperExecutionTargetResolution(
    WorkbenchWorkspaceContext? Context,
    string? RootPath,
    DotNetProjectInfo? Project,
    string? ErrorCode,
    string? Error);

internal sealed record DeveloperTestDebugTargetResolution(
    WorkbenchWorkspaceContext? Context,
    string? RootPath,
    DotNetProjectInfo? Project,
    DeveloperTestSourcePath? Source,
    DeveloperTestSourceLine? Line,
    string? ErrorCode,
    string? Error);

internal interface IDeveloperExecutionTargetResolver
{
    ValueTask<DeveloperExecutionTargetResolution> ResolveDebugTargetAsync(
        WorkbenchWorkspaceRequest workspace,
        WorkbenchExecutionTarget target,
        DeveloperRunOverrides? runOverrides,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperTestDebugTargetResolution> ResolveTestDebugTargetAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperProjectTarget project,
        DeveloperTestTarget test,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new DeveloperTestDebugTargetResolution(
                null, null, null, null, null, "test_debug_not_supported",
                "Owned Test Debug target resolution is unavailable."));
}
