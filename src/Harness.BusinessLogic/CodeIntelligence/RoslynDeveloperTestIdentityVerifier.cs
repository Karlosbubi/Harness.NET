using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed class RoslynDeveloperTestIdentityVerifier(
    IWorkbenchCodeIntelligenceService code) : IDeveloperTestIdentityVerifier
{
    public async ValueTask<DeveloperTestIdentityVerification> VerifyExactAsync(
        WorkbenchWorkspaceRequest workspace,
        DeveloperProjectTarget project,
        DeveloperTestTarget test,
        CancellationToken cancellationToken = default)
    {
        if (test.Scope is not DeveloperTestScope.Exact)
            return Failure("test_debug_scope_invalid", "Test Debug requires one exact source test.");
        WorkbenchCodeSessionId? sessionId = null;
        try
        {
            WorkbenchCodeSessionView session = await code.StartAsync(new(
                workspace.WorkspaceId,
                workspace.GoalId,
                new(project.ProjectPath.Value)), progress: null, cancellationToken);
            sessionId = session.SessionId;
            if (sessionId is null || session.State is WorkbenchCodeResultState.Failed)
                return Failure("test_debug_roslyn_unavailable",
                    session.Issues.FirstOrDefault()?.Message.Value ??
                    "The exact Roslyn test context is unavailable.");
            WorkbenchCodeTestDiscoveryView discovery = await code.DiscoverTestsAsync(new(
                sessionId,
                test.FullyQualifiedName.Value,
                MaximumResults: 50,
                Offset: 0), cancellationToken);
            WorkbenchCodeTestCase[] matches = discovery.Tests.Where(item =>
                item.Id.Value.Equals(test.Id.Value, StringComparison.Ordinal) &&
                item.FullyQualifiedName.Value.Equals(
                    test.FullyQualifiedName.Value, StringComparison.Ordinal) &&
                item.ProjectPath.Value.Equals(project.ProjectPath.Value, StringComparison.Ordinal))
                .Take(2).ToArray();
            if (matches.Length != 1)
                return Failure("test_debug_target_stale",
                    "The exact compiler-discovered test identity is stale or unavailable.");
            WorkbenchCodeTestCase exact = matches[0];
            return new(true, new(exact.Path.Value), new(exact.Range.Start.Line + 1), null, null);
        }
        finally
        {
            if (sessionId is not null)
                await code.StopAsync(sessionId, CancellationToken.None);
        }
    }

    private static DeveloperTestIdentityVerification Failure(string code, string message) =>
        new(false, null, null, code, message);
}
