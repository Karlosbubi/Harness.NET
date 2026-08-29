using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Execution;

internal sealed record DeveloperTestSourcePath(string Value);
internal sealed record DeveloperTestSourceLine(int Value);

internal sealed record DeveloperTestIdentityVerification(
    bool IsVerified,
    DeveloperTestSourcePath? Source,
    DeveloperTestSourceLine? Line,
    string? ErrorCode,
    string? Error);

internal interface IDeveloperTestIdentityVerifier
{
    ValueTask<DeveloperTestIdentityVerification> VerifyExactAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperProjectTarget project,
        DeveloperTestTarget test,
        CancellationToken cancellationToken = default);
}
