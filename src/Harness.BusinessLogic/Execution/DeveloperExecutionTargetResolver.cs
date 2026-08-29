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

internal interface IDeveloperExecutionTargetResolver
{
    ValueTask<DeveloperExecutionTargetResolution> ResolveDebugTargetAsync(
        WorkbenchWorkspaceRequest workspace,
        WorkbenchExecutionTarget target,
        DeveloperRunOverrides? runOverrides,
        CancellationToken cancellationToken = default);
}
