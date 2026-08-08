using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Models;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class GoalModelServiceTests
{
    [Fact]
    public async Task Discovers_only_chat_models_with_access_and_pricing_metadata()
    {
        GoalModelService service = CreateService(
            Goal(remoteBudget: 2_000_000),
            new CatalogProvider([
                Model("chat", ModelPurpose.Chat),
                Model("embed", ModelPurpose.Embedding),
            ]),
            new CatalogProvider([], new("credential_missing", "Missing credential", false)));

        GoalModelCatalog catalog = await service.DiscoverAsync(new("goal-1"));

        GoalModelCandidate candidate = Assert.Single(catalog.Models);
        Assert.Equal("Ollama", candidate.Provider.Value);
        Assert.Equal("chat", candidate.Model.Value);
        Assert.Equal(ModelAccess.Local, candidate.Access);
        Assert.Equal(1m, candidate.InputPrice?.Value);
        GoalModelProviderIssue issue = Assert.Single(catalog.Issues);
        Assert.Equal("OpenRouter", issue.Provider.Value);
        Assert.Equal("credential_missing", issue.Code);
    }

    [Fact]
    public async Task Local_defaults_are_effective_without_granting_implicit_remote_authority()
    {
        GoalModelService service = CreateService(
            Goal(remoteBudget: 2_000_000),
            new CatalogProvider([Model("local", ModelPurpose.Chat)]),
            new CatalogProvider([Model("remote", ModelPurpose.Chat)]),
            defaultLeadProvider: "OpenRouter");

        GoalModelSelectionView lead = (await service.GetSelectionsAsync(new("goal-1")))
            .Single(item => item.Role is AgentRole.Lead);
        GoalModelRouteResult route = await service.ResolveAsync(new("goal-1"), AgentRole.Lead);

        Assert.False(lead.IsExplicit);
        Assert.Equal(ModelAccess.Remote, lead.Access);
        Assert.Null(route.Route);
        Assert.Equal("remote_model_not_selected", route.ErrorCode?.Value);
    }

    [Fact]
    public async Task Explicit_remote_selection_requires_a_cap_and_resolves_only_for_the_goal_role()
    {
        MemorySelectionStore selections = new();
        GoalModelService localOnly = CreateService(
            Goal(remoteBudget: null),
            new CatalogProvider([Model("local", ModelPurpose.Chat)]),
            new CatalogProvider([Model("remote", ModelPurpose.Chat)]),
            selections);

        GoalModelSelectionResult rejected = await localOnly.SelectAsync(new(
            new("goal-1"),
            AgentRole.Reviewer,
            new("OpenRouter"),
            new("remote")));

        GoalModelService capped = CreateService(
            Goal(remoteBudget: 2_000_000),
            new CatalogProvider([Model("local", ModelPurpose.Chat)]),
            new CatalogProvider([Model("remote", ModelPurpose.Chat)]),
            selections);
        GoalModelSelectionResult selected = await capped.SelectAsync(new(
            new("goal-1"),
            AgentRole.Reviewer,
            new("OpenRouter"),
            new("remote")));
        GoalModelRouteResult reviewer = await capped.ResolveAsync(
            new("goal-1"),
            AgentRole.Reviewer);
        GoalModelRouteResult implementer = await capped.ResolveAsync(
            new("goal-1"),
            AgentRole.Implementer);

        Assert.Equal("remote_budget_required", rejected.ErrorCode);
        Assert.True(selected.Selection?.IsExplicit);
        Assert.Equal(ModelAccess.Remote, reviewer.Route?.Access);
        Assert.Equal("remote", reviewer.Route?.Model.Value);
        Assert.Equal(ModelAccess.Local, implementer.Route?.Access);
        Assert.Equal("local", implementer.Route?.Model.Value);
    }

    [Fact]
    public async Task Rejects_an_embedding_model_for_an_agent_role()
    {
        GoalModelService service = CreateService(
            Goal(remoteBudget: 1_000_000),
            new CatalogProvider([Model("embed", ModelPurpose.Embedding)]),
            new CatalogProvider([]));

        GoalModelSelectionResult result = await service.SelectAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("Ollama"),
            new("embed")));

        Assert.Equal("model_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task Persists_typed_role_defaults_without_granting_remote_goal_authority()
    {
        MemoryDefaultStore defaults = new();
        GoalModelService service = CreateService(
            Goal(remoteBudget: 2_000_000),
            new CatalogProvider([Model("local", ModelPurpose.Chat)]),
            new CatalogProvider([Model("remote", ModelPurpose.Chat)]),
            defaults: defaults);

        AgentRoleDefaultUpdateResult updated = await service.UpdateAsync(new(
            AgentRole.Lead,
            new("OpenRouter"),
            new("remote")));
        AgentDefaultsSnapshot snapshot = await service.GetAsync();
        GoalModelRouteResult route = await service.ResolveAsync(new("goal-1"), AgentRole.Lead);

        Assert.NotNull(updated.Value);
        Assert.True(snapshot.Roles.Single(item => item.Role is AgentRole.Lead).IsPersisted);
        Assert.Equal("remote", snapshot.Roles.Single(item => item.Role is AgentRole.Lead).Model.Value);
        Assert.Equal("remote_model_not_selected", route.ErrorCode?.Value);
    }

    [Fact]
    public async Task Default_discovery_does_not_require_a_goal_or_workspace()
    {
        GoalModelService service = CreateService(
            Goal(remoteBudget: null),
            new CatalogProvider([Model("local", ModelPurpose.Chat)]),
            new CatalogProvider([Model("remote", ModelPurpose.Chat)]));

        AgentDefaultsSnapshot snapshot = await service.DiscoverAvailableAsync();

        Assert.Equal(3, snapshot.Roles.Count);
        Assert.Equal(2, snapshot.Models.Count);
        Assert.Empty(snapshot.DefaultIssues);
        Assert.All(snapshot.Models, item => Assert.Equal(Enum.GetValues<AgentRole>(), item.SupportedRoles));
        Assert.Collection(
            snapshot.Providers,
            provider =>
            {
                Assert.Equal("Ollama", provider.Provider.Value);
                Assert.Equal(AgentModelProviderAvailability.Available, provider.Availability);
                Assert.Equal(1, provider.RoleCompatibleModels);
            },
            provider =>
            {
                Assert.Equal("OpenRouter", provider.Provider.Value);
                Assert.Equal(AgentModelProviderAvailability.Available, provider.Availability);
                Assert.True(provider.HasPublishedPricing);
            });
    }

    [Fact]
    public async Task Discovery_qualifies_models_against_each_role_capability_policy()
    {
        GoalModelService service = CreateService(
            Goal(remoteBudget: null),
            new CatalogProvider([
                Model("qualified", ModelPurpose.Chat),
                Model("plain-chat", ModelPurpose.Chat, ["completion"]),
            ]),
            new CatalogProvider([]));

        AgentDefaultsSnapshot snapshot = await service.DiscoverAvailableAsync();

        GoalModelCandidate qualified = snapshot.Models.Single(model => model.Model.Value == "qualified");
        GoalModelCandidate plainChat = snapshot.Models.Single(model => model.Model.Value == "plain-chat");
        Assert.Equal(Enum.GetValues<AgentRole>(), qualified.SupportedRoles);
        Assert.Empty(plainChat.SupportedRoles);
        Assert.Equal(2, snapshot.Providers.Single(provider => provider.Provider.Value == "Ollama")
            .DiscoveredChatModels);
        Assert.Equal(1, snapshot.Providers.Single(provider => provider.Provider.Value == "Ollama")
            .RoleCompatibleModels);
    }

    [Fact]
    public async Task Rejects_a_chat_model_that_does_not_fully_support_the_requested_role()
    {
        MemorySelectionStore selections = new();
        MemoryDefaultStore defaults = new();
        GoalModelService service = CreateService(
            Goal(remoteBudget: null),
            new CatalogProvider([Model("plain-chat", ModelPurpose.Chat, ["completion"])]),
            new CatalogProvider([]),
            selections,
            defaults: defaults);

        GoalModelSelectionResult selected = await service.SelectAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("Ollama"),
            new("plain-chat")));
        AgentRoleDefaultUpdateResult updated = await service.UpdateAsync(new(
            AgentRole.Reviewer,
            new("Ollama"),
            new("plain-chat")));

        Assert.Equal("model_role_unsupported", selected.ErrorCode);
        Assert.Equal("model_role_unsupported", updated.ErrorCode);
        Assert.Empty(await selections.ListAsync("goal-1"));
        Assert.Empty(await defaults.ListAsync());
    }

    [Fact]
    public async Task Startup_discovery_reports_incompatible_persisted_defaults()
    {
        MemoryDefaultStore defaults = new();
        await defaults.SaveAsync(new(
            AgentDefaultRole.Lead,
            new("Ollama"),
            new("plain-chat"),
            DateTimeOffset.UtcNow));
        GoalModelService service = CreateService(
            Goal(remoteBudget: null),
            new CatalogProvider([Model("plain-chat", ModelPurpose.Chat, ["completion"])]),
            new CatalogProvider([]),
            defaults: defaults);

        AgentDefaultsSnapshot snapshot = await service.DiscoverAvailableAsync();

        AgentRoleDefaultIssue issue = Assert.Single(snapshot.DefaultIssues, candidate =>
            candidate.Role is AgentRole.Lead);
        Assert.Equal(AgentRole.Lead, issue.Role);
        Assert.Equal("default_model_role_unsupported", issue.Code.Value);
    }

    [Fact]
    public async Task Selection_reuses_the_startup_catalog_snapshot()
    {
        CatalogProvider local = new([Model("local", ModelPurpose.Chat)]);
        CatalogProvider remote = new([Model("remote", ModelPurpose.Chat)]);
        GoalModelService service = CreateService(Goal(remoteBudget: null), local, remote);

        await service.DiscoverAvailableAsync();
        GoalModelSelectionResult result = await service.SelectAsync(new(
            new("goal-1"),
            AgentRole.Lead,
            new("Ollama"),
            new("local")));

        Assert.NotNull(result.Selection);
        Assert.Equal(1, local.CallCount);
        Assert.Equal(1, remote.CallCount);
    }

    [Fact]
    public async Task Missing_persisted_provider_falls_back_without_breaking_startup()
    {
        MemoryDefaultStore defaults = new();
        await defaults.SaveAsync(new(
            AgentDefaultRole.Lead,
            new("removed-provider"),
            new("removed-model"),
            DateTimeOffset.UtcNow));
        GoalModelService service = CreateService(
            Goal(remoteBudget: null),
            new CatalogProvider([Model("local", ModelPurpose.Chat)]),
            new CatalogProvider([]),
            defaults: defaults);

        AgentRoleDefault lead = (await service.GetAsync()).Roles
            .Single(item => item.Role is AgentRole.Lead);

        Assert.Equal("Ollama", lead.Provider.Value);
        Assert.False(lead.IsPersisted);
    }

    private static GoalModelService CreateService(
        StoredGoal goal,
        IModelProvider local,
        IModelProvider remote,
        MemorySelectionStore? selections = null,
        string defaultLeadProvider = "Ollama",
        MemoryDefaultStore? defaults = null) => new(
        new StubGoalStore(goal),
        new StubWorkspaceStore(Workspace()),
        selections ?? new(),
        defaults ?? new(),
        [
            new(new("Ollama"), ModelAccess.Local, new("local"), local),
            new(new("OpenRouter"), ModelAccess.Remote, new("remote"), remote),
        ],
        new Dictionary<AgentRole, ModelProviderName>
        {
            [AgentRole.Lead] = new(defaultLeadProvider),
            [AgentRole.Implementer] = new("Ollama"),
            [AgentRole.Reviewer] = new("Ollama"),
        },
        TimeProvider.System);

    private static ModelDescriptor Model(
        string id,
        ModelPurpose purpose,
        IReadOnlyList<string>? capabilities = null) => new(
        id,
        "provider",
        Family: null,
        ParameterSize: null,
        Quantization: null,
        Capabilities: capabilities ?? ["tools"],
        ContextLength: 128_000,
        Pricing: new(0.000001m, 0.000002m, 0m),
        Purposes: [purpose]);

    private static StoredGoal Goal(long? remoteBudget) => new(
        "goal-1",
        "workspace-1",
        "Goal",
        "Objective",
        3,
        remoteBudget,
        "Draft",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static RegisteredWorkspace Workspace() => new(
        "workspace-1",
        "/workspace/repository",
        "repository",
        "/workspace/repository/Repository.slnx",
        IsTrusted: true,
        IsActive: true,
        "main",
        IsDirty: false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class CatalogProvider(
        IReadOnlyList<ModelDescriptor> models,
        ProviderError? error = null) : IModelProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelCatalog(models, error));
        }

        public IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EmbeddingResult> EmbedAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemorySelectionStore : IGoalModelSelectionStore
    {
        private readonly Dictionary<(string GoalId, string Role), StoredGoalModelSelection> items = [];

        public ValueTask<StoredGoalModelSelection> SaveAsync(
            StoredGoalModelSelection selection,
            CancellationToken cancellationToken = default)
        {
            items[(selection.GoalId, selection.Role)] = selection;
            return ValueTask.FromResult(selection);
        }

        public ValueTask<IReadOnlyList<StoredGoalModelSelection>> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredGoalModelSelection>>(
                items.Values.Where(item => item.GoalId == goalId).ToArray());
    }

    private sealed class MemoryDefaultStore : IAgentRoleDefaultStore
    {
        private readonly Dictionary<AgentDefaultRole, StoredAgentRoleDefault> items = [];

        public ValueTask<IReadOnlyList<StoredAgentRoleDefault>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredAgentRoleDefault>>(items.Values.ToArray());

        public ValueTask<StoredAgentRoleDefault> SaveAsync(
            StoredAgentRoleDefault value,
            CancellationToken cancellationToken = default)
        {
            items[value.Role] = value;
            return ValueTask.FromResult(value);
        }
    }

    private sealed class StubGoalStore(StoredGoal goal) : IGoalStore
    {
        public ValueTask<StoredGoal> CreateAsync(
            StoredGoal value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredGoal?> GetAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StoredGoal?>(goal.Id == goalId ? goal : null);

        public ValueTask<IReadOnlyList<StoredGoal>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredGoal?> UpdateDraftSettingsAsync(
            string goalId, DateTimeOffset expectedUpdatedAt, int reviewCycleLimit,
            long? remoteBudgetMicrousd, DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StoredGoalBudgetExtensionSnapshot?> ExtendRemoteBudgetAsync(
            string extensionId, string goalId, long? expectedBudgetMicrousd,
            long newBudgetMicrousd, string reason, DateTimeOffset approvedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredPlan?> GetCurrentPlanAsync(
            string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredPlanSnapshot> SavePlanAsync(
            StoredPlan plan,
            string expectedGoalState,
            string nextGoalState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredPlanSnapshot> DecidePlanAsync(
            StoredApproval approval,
            StoredGoalWorktree? worktree,
            string expectedGoalState,
            string expectedPlanState,
            string nextGoalState,
            string nextPlanState,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<StoredGoalWorktree?> GetWorktreeAsync(
            string goalId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubWorkspaceStore(RegisteredWorkspace workspace) : IWorkspaceStore
    {
        public ValueTask<RegisteredWorkspace?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RegisteredWorkspace?>(workspace);

        public ValueTask<RegisteredWorkspace> SaveAsync(
            WorkspaceInspection inspection,
            string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace?> FindByPathAsync(
            string rootPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RegisteredWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetActiveAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RegisteredWorkspace> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
