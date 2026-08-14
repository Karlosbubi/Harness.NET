using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Tests.Inspection;

public sealed class DeveloperGitServiceTests
{
    [Fact]
    public async Task Resolves_approved_goal_context_and_preserves_exact_baseline()
    {
        Repository repository = new();
        DeveloperGitService service = new(
            new ContextResolver(),
            repository);

        DeveloperGitIndexCommandResult result = await service.UpdateIndexAsync(new(
            new(new("workspace-id"), new("goal-id")),
            new("expected-fingerprint"),
            DeveloperGitIndexAction.Stage,
            [new("src/App.cs")]));

        Assert.Equal(WorkbenchWorkspaceScope.ApprovedGoalWorktree, result.Context.Scope);
        Assert.Equal("/state/worktrees/goal-id", repository.Request!.RepositoryRoot);
        Assert.Equal("expected-fingerprint", repository.Request.ExpectedFingerprint.Value);
        Assert.Equal(DeveloperGitIndexOperation.Stage, repository.Request.Operation);
        Assert.Equal("src/App.cs", Assert.Single(repository.Request.Paths).Value);
    }

    private sealed class Repository : IDeveloperGitRepository
    {
        internal DeveloperGitIndexRequest? Request { get; private set; }

        public ValueTask<DeveloperGitIndexResult> UpdateIndexAsync(
            DeveloperGitIndexRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new DeveloperGitIndexResult(null, [], null, null));
        }
    }

    private sealed class ContextResolver : IWorkbenchWorkspaceContextResolver
    {
        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchWorkspaceResolution(
                new(request.WorkspaceId, request.GoalId, new("harness/goal"),
                    WorkbenchWorkspaceScope.ApprovedGoalWorktree, "Approved goal worktree"),
                "/state/worktrees/goal-id",
                null,
                null));
    }
}
