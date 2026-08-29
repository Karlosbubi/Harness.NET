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

    async ValueTask<DeveloperTestDebugTargetResolution>
        IDeveloperExecutionTargetResolver.ResolveTestDebugTargetAsync(
            WorkbenchWorkspaceRequest workspace,
            DeveloperProjectTarget project,
            DeveloperTestTarget test,
            CancellationToken cancellationToken)
    {
        if (testIdentityVerifier is null)
            return new(null, null, null, null, null, "test_debug_roslyn_unavailable",
                "The Roslyn test identity verifier is unavailable.");
        if (!IsValidTest(test, project) || test.Scope is not DeveloperTestScope.Exact)
            return new(null, null, null, null, null, "test_debug_target_invalid",
                "Select one exact compiler-discovered test.");
        Resolution resolution = await ResolveProjectAsync(workspace, project, cancellationToken);
        if (resolution.Context is null || resolution.RootPath is null || resolution.Project is null)
            return new(null, null, null, null, null,
                resolution.ErrorCode, resolution.Error);
        DeveloperTestIdentityVerification verified = await testIdentityVerifier.VerifyExactAsync(
            workspace, project, test, cancellationToken);
        return verified.IsVerified
            ? new(resolution.Context, resolution.RootPath, resolution.Project,
                verified.Source, verified.Line, null, null)
            : new(null, null, null, null, null, verified.ErrorCode, verified.Error);
    }
}
