using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Execution;

internal sealed partial class DeveloperProjectExecutionService
{
    async ValueTask<DeveloperExecutionTargetResolution>
        IDeveloperExecutionTargetResolver.ResolveDebugTargetAsync(
            WorkbenchWorkspaceRequest workspace,
            WorkbenchExecutionTarget target,
            DeveloperRunOverrides? runOverrides,
            CancellationToken cancellationToken)
    {
        Resolution resolution = await ResolveAsync(workspace, target, cancellationToken);
        string? overrideError = ValidateOverrides(runOverrides, resolution.Project);
        return overrideError is null
            ? new(resolution.Context, resolution.RootPath, resolution.Project,
                resolution.ErrorCode, resolution.Error)
            : new(null, null, null, "run_overrides_invalid", overrideError);
    }
}
