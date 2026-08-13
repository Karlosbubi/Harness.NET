using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.ProjectSecrets;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Tests.ProjectSecrets;

public sealed class ProjectUserSecretsServiceTests
{
    [Fact]
    public async Task Lists_only_project_metadata_and_preserves_unavailable_state()
    {
        Store store = new()
        {
            Descriptor = new("src/App/App.csproj",
                StoredProjectUserSecretsState.UserSecretsIdMissing, 0,
                "user_secrets_id_missing", "Add UserSecretsId."),
        };
        ProjectUserSecretsService service = CreateService(store, new SensitiveDisplayGuard());

        ProjectUserSecretsProjectListResult result = await service.ListProjectsAsync(
            new("workspace-a"));

        ProjectUserSecretsProjectView project = Assert.Single(result.Projects);
        Assert.Equal(ProjectUserSecretsProjectState.UserSecretsIdMissing, project.State);
        Assert.Equal("src/App/App.csproj", project.Path.Value);
        Assert.Empty(store.ValuesReturnedToList);
    }

    [Fact]
    public async Task Reveal_blocks_visual_capture_until_the_disclosure_is_disposed()
    {
        Store store = new();
        SensitiveDisplayGuard guard = new();
        ProjectUserSecretsService service = CreateService(store, guard);

        ProjectUserSecretRevealResult revealed = await service.RevealAsync(
            new("workspace-a"), new("src/App/App.csproj"), new("ApiKey"));

        Assert.Equal(ProjectUserSecretValueOutcome.Succeeded, revealed.Outcome);
        Assert.NotNull(revealed.Disclosure);
        Assert.False(guard.TryBeginVisualCapture(out _));
        Assert.DoesNotContain("very-secret", revealed.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", revealed.Disclosure!.ToString(), StringComparison.Ordinal);

        revealed.Disclosure.Dispose();
        Assert.True(guard.TryBeginVisualCapture(out ISensitiveDisplayLease? capture));
        Assert.False(guard.TryBeginSensitiveDisplay(
            SensitiveDisplayKind.ProjectUserSecret, out _));
        capture!.Dispose();
    }

    [Fact]
    public async Task Copy_is_transient_and_does_not_open_a_disclosure_lease()
    {
        SensitiveDisplayGuard guard = new();
        ProjectUserSecretsService service = CreateService(new Store(), guard);

        ProjectUserSecretCopyResult copied = await service.CopyAsync(
            new("workspace-a"), new("src/App/App.csproj"), new("ApiKey"));

        Assert.Equal("very-secret", copied.Value?.Value);
        Assert.DoesNotContain("very-secret", copied.ToString(), StringComparison.Ordinal);
        Assert.True(guard.TryBeginVisualCapture(out ISensitiveDisplayLease? capture));
        capture!.Dispose();
    }

    [Fact]
    public async Task Add_change_and_delete_call_separate_store_operations()
    {
        Store store = new();
        ProjectUserSecretsService service = CreateService(store, new SensitiveDisplayGuard());

        await service.AddAsync(new("workspace-a"), new("src/App/App.csproj"),
            new("ApiKey"), new("one"));
        await service.ChangeAsync(new("workspace-a"), new("src/App/App.csproj"),
            new("ApiKey"), new("two"));
        await service.DeleteAsync(new("workspace-a"), new("src/App/App.csproj"),
            new("ApiKey"));

        Assert.Equal((1, 1, 1), (store.AddCalls, store.ChangeCalls, store.DeleteCalls));
    }

    [Fact]
    public async Task Untrusted_workspace_is_rejected_before_store_access()
    {
        Store store = new();
        ProjectUserSecretsService service = new(
            new Workspaces(isTrusted: false), new DotNet(), store, new SensitiveDisplayGuard());

        ProjectUserSecretsProjectListResult result = await service.ListProjectsAsync(
            new("workspace-a"));

        Assert.Equal("workspace_not_trusted", result.ErrorCode);
        Assert.Equal(0, store.DescribeCalls);
    }

    private static ProjectUserSecretsService CreateService(
        Store store,
        SensitiveDisplayGuard guard) => new(new Workspaces(), new DotNet(), store, guard);

    private sealed class DotNet : IWorkspaceDotNetInspector
    {
        public ValueTask<WorkspaceDotNetInfo> InspectAsync(
            string workspaceRoot,
            string entryPoint,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new WorkspaceDotNetInfo(
            "Harness.slnx", "slnx", null,
            [new("src/App/App.csproj", "Microsoft.NET.Sdk", ["net10.0"], null, null, [])],
            false, null, null));
    }

    private sealed class Workspaces(bool isTrusted = true) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(new("workspace-a", "/workspace", "Workspace",
                "Harness.slnx", isTrusted, true, "main", false,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        public ValueTask<RegisteredWorkspace> SaveAsync(WorkspaceInspection inspection, string entryPoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace?> FindByPathAsync(string rootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetActiveAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RegisteredWorkspace> SetTrustAsync(string workspaceId, bool trusted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Store : IProjectUserSecretStore
    {
        public StoredProjectUserSecretsDescriptor Descriptor { get; init; } = new(
            "src/App/App.csproj", StoredProjectUserSecretsState.Available, 1, null, null);
        public List<string> ValuesReturnedToList { get; } = [];
        public int DescribeCalls { get; private set; }
        public int AddCalls { get; private set; }
        public int ChangeCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public ValueTask<StoredProjectUserSecretsDescriptor> DescribeAsync(StoredProjectUserSecretsRequest request, CancellationToken cancellationToken = default)
        {
            DescribeCalls++;
            return ValueTask.FromResult(Descriptor);
        }
        public ValueTask<StoredProjectUserSecretList> ListAsync(StoredProjectUserSecretsRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new StoredProjectUserSecretList(Descriptor,
                [new("ApiKey")]));
        public ValueTask<StoredProjectUserSecretReadResult> ReadAsync(StoredProjectUserSecretsRequest request, StoredProjectUserSecretKey key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new StoredProjectUserSecretReadResult(
                StoredProjectUserSecretReadState.Succeeded, new("very-secret"), null, null));
        public ValueTask<StoredProjectUserSecretMutationResult> AddAsync(StoredProjectUserSecretsRequest request, StoredProjectUserSecretKey key, StoredProjectUserSecretValue value, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            return Success();
        }
        public ValueTask<StoredProjectUserSecretMutationResult> ChangeAsync(StoredProjectUserSecretsRequest request, StoredProjectUserSecretKey key, StoredProjectUserSecretValue value, CancellationToken cancellationToken = default)
        {
            ChangeCalls++;
            return Success();
        }
        public ValueTask<StoredProjectUserSecretMutationResult> DeleteAsync(StoredProjectUserSecretsRequest request, StoredProjectUserSecretKey key, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Success();
        }
        private ValueTask<StoredProjectUserSecretMutationResult> Success() => ValueTask.FromResult(
            new StoredProjectUserSecretMutationResult(
                StoredProjectUserSecretMutationState.Succeeded, Descriptor, null, null));
    }
}
