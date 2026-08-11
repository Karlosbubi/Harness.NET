using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Models;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Agents;

internal sealed class GoalModelService :
    IGoalModelService,
    IGoalModelRouteResolver,
    IAgentDefaultsService
{
    private readonly IGoalStore goalStore;
    private readonly IWorkspaceStore workspaceStore;
    private readonly IGoalModelSelectionStore selectionStore;
    private readonly IAgentRoleDefaultStore defaultStore;
    private readonly IReadOnlyDictionary<string, GoalModelProviderRegistration> providers;
    private readonly IReadOnlyDictionary<AgentRole, ModelProviderName> defaultRoutes;
    private readonly TimeProvider timeProvider;
    private volatile ModelDiscoverySnapshot? discovery;

    internal GoalModelService(
        IGoalStore goalStore,
        IWorkspaceStore workspaceStore,
        IGoalModelSelectionStore selectionStore,
        IAgentRoleDefaultStore defaultStore,
        IReadOnlyList<GoalModelProviderRegistration> providers,
        IReadOnlyDictionary<AgentRole, ModelProviderName> defaultRoutes,
        TimeProvider timeProvider)
    {
        this.goalStore = goalStore;
        this.workspaceStore = workspaceStore;
        this.selectionStore = selectionStore;
        this.defaultStore = defaultStore;
        this.providers = providers.ToDictionary(
            registration => registration.Name.Value,
            StringComparer.OrdinalIgnoreCase);
        this.defaultRoutes = defaultRoutes;
        this.timeProvider = timeProvider;

        AgentRole[] missingRoles = Enum.GetValues<AgentRole>()
            .Where(role => !defaultRoutes.ContainsKey(role))
            .ToArray();
        if (missingRoles.Length > 0)
        {
            throw new ArgumentException(
                $"Missing default model routes: {string.Join(", ", missingRoles)}.",
                nameof(defaultRoutes));
        }

        ModelProviderName[] missingProviders = defaultRoutes.Values
            .Where(provider => !this.providers.ContainsKey(provider.Value))
            .Distinct()
            .ToArray();
        if (missingProviders.Length > 0)
        {
            throw new ArgumentException(
                $"Default routes reference missing providers: " +
                $"{string.Join(", ", missingProviders.Select(provider => provider.Value))}.",
                nameof(defaultRoutes));
        }
    }

    public async ValueTask<GoalModelCatalog> DiscoverAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        StoredGoal? goal = await GetActiveGoalAsync(goalId, cancellationToken);
        if (goal is null)
        {
            return new(
                goalId,
                [],
                [],
                "goal_not_active",
                "Model discovery requires a goal in the active workspace.");
        }

        ModelDiscoverySnapshot discovered = await DiscoverModelsAsync(
            forceRefresh: true,
            cancellationToken);

        return new(
            goalId,
            discovered.Models,
            discovered.Issues,
            ErrorCode: null,
            Error: null);
    }

    public async ValueTask<IReadOnlyList<GoalModelSelectionView>> GetSelectionsAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        if (await GetActiveGoalAsync(goalId, cancellationToken) is null)
        {
            return [];
        }

        IReadOnlyDictionary<AgentRole, StoredGoalModelSelection> stored =
            (await selectionStore.ListAsync(goalId.Value, cancellationToken))
            .ToDictionary(selection => ParseRole(selection.Role));
        IReadOnlyDictionary<AgentRole, AgentRoleDefault> defaults =
            await DefaultsAsync(cancellationToken);
        return Enum.GetValues<AgentRole>().Select(role =>
        {
            if (stored.TryGetValue(role, out StoredGoalModelSelection? selected))
            {
                GoalModelProviderRegistration provider = Provider(selected.Provider);
                return new GoalModelSelectionView(
                    goalId,
                    role,
                    provider.Name,
                    new(selected.Model),
                    provider.Access,
                    IsExplicit: true,
                    selected.SelectedAt);
            }

            AgentRoleDefault effectiveDefault = defaults[role];
            return new(
                goalId,
                role,
                effectiveDefault.Provider,
                effectiveDefault.Model,
                effectiveDefault.Access,
                IsExplicit: false,
                SelectedAt: null);
        }).ToArray();
    }

    public async ValueTask<GoalModelSelectionResult> SelectAsync(
        GoalModelSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.GoalId is null ||
            string.IsNullOrWhiteSpace(request.GoalId.Value) ||
            !Enum.IsDefined(request.Role) ||
            request.Provider is null ||
            string.IsNullOrWhiteSpace(request.Provider.Value) ||
            request.Model is null ||
            string.IsNullOrWhiteSpace(request.Model.Value))
        {
            return Failure("invalid_model_selection", "A goal, role, provider, and model are required.");
        }

        StoredGoal? goal = await GetActiveGoalAsync(request.GoalId, cancellationToken);
        if (goal is null)
        {
            return Failure("goal_not_active", "Model selection requires a goal in the active workspace.");
        }

        if (!providers.TryGetValue(request.Provider.Value, out GoalModelProviderRegistration? provider))
        {
            return Failure("provider_missing", $"Provider '{request.Provider.Value}' is not configured.");
        }

        if (provider.Access is ModelAccess.Remote && goal.RemoteBudgetMicrousd is null)
        {
            return Failure(
                "remote_budget_required",
                "Choose unlimited or capped remote spending for the goal before authorizing a remote model.");
        }

        ModelDiscoverySnapshot discovered = await DiscoverModelsAsync(
            forceRefresh: false,
            cancellationToken);
        GoalModelCandidate? model = FindModel(discovered, request.Provider, request.Model);
        if (model is null)
        {
            return Failure(
                "model_unavailable",
                $"Chat model '{request.Model.Value}' is unavailable from '{provider.Name.Value}'.");
        }

        if (!model.SupportedRoles.Contains(request.Role))
        {
            return Failure(
                "model_role_unsupported",
                $"Model '{provider.Name.Value}/{request.Model.Value}' does not fully support {request.Role}; " +
                $"required capabilities: {string.Join(", ", AgentRoleModelPolicy.RequiredCapabilities(request.Role))}.");
        }

        StoredGoalModelSelection stored = await selectionStore.SaveAsync(new(
            goal.Id,
            request.Role.ToString(),
            provider.Name.Value,
            model.Model.Value,
            timeProvider.GetUtcNow()), cancellationToken);
        return new(
            new(
                request.GoalId,
                request.Role,
                provider.Name,
                new(stored.Model),
                provider.Access,
                IsExplicit: true,
                stored.SelectedAt),
            ErrorCode: null,
            Error: null);
    }

    public async ValueTask<GoalModelRouteResult> ResolveAsync(
        GoalId goalId,
        AgentRole role,
        CancellationToken cancellationToken = default)
    {
        if (goalId is null || string.IsNullOrWhiteSpace(goalId.Value) || !Enum.IsDefined(role))
        {
            return RouteFailure("invalid_agent_request", "A valid goal and role are required.");
        }

        StoredGoal? goal = await GetActiveGoalAsync(goalId, cancellationToken);
        if (goal is null)
        {
            return RouteFailure("goal_not_active", "Agent execution requires a goal in the active workspace.");
        }

        StoredGoalModelSelection? selected = (await selectionStore.ListAsync(
                goalId.Value,
                cancellationToken))
            .SingleOrDefault(item => ParseRole(item.Role) == role);
        GoalModelProviderRegistration provider;
        AgentModel model;
        if (selected is null)
        {
            AgentRoleDefault effectiveDefault = await DefaultAsync(role, cancellationToken);
            provider = Provider(effectiveDefault.Provider.Value);
            model = effectiveDefault.Model;
            if (provider.Access is ModelAccess.Remote)
            {
                return RouteFailure(
                    "remote_model_not_selected",
                    "Remote agent execution requires an explicit model selection for this goal and role.");
            }
        }
        else
        {
            provider = Provider(selected.Provider);
            model = new(selected.Model);
        }

        if (provider.Access is ModelAccess.Remote && goal.RemoteBudgetMicrousd is null)
        {
            return RouteFailure(
                "remote_budget_required",
                "Remote agent execution requires unlimited or capped goal spending authority.");
        }


        ModelDiscoverySnapshot discovered = await DiscoverModelsAsync(
            forceRefresh: false,
            cancellationToken);
        GoalModelCandidate? available = FindModel(discovered, provider.Name, model);
        if (available is null)
        {
            return RouteFailure(
                "model_unavailable",
                $"Configured model '{provider.Name.Value}/{model.Value}' is not currently available.");
        }

        if (!available.SupportedRoles.Contains(role))
        {
            return RouteFailure(
                "model_role_unsupported",
                $"Configured model '{provider.Name.Value}/{model.Value}' does not fully support {role}.");
        }

        return new(
            new(goalId, role, provider.Name, model, provider.Access, provider.Provider),
            ErrorCode: null,
            Error: null);
    }

    public async ValueTask<AgentDefaultsSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        AgentRoleDefault[] defaults = (await DefaultsAsync(cancellationToken)).Values
            .OrderBy(item => item.Role)
            .ToArray();
        ModelDiscoverySnapshot? current = discovery;
        return current is null
            ? new(defaults, [], [], UndiscoveredProviders(), [])
            : Snapshot(defaults, current);
    }

    public async ValueTask<AgentDefaultsSnapshot> DiscoverAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        ModelDiscoverySnapshot discovered = await DiscoverModelsAsync(
            forceRefresh: true,
            cancellationToken);
        AgentRoleDefault[] defaults = (await DefaultsAsync(cancellationToken)).Values
            .OrderBy(item => item.Role)
            .ToArray();
        return Snapshot(defaults, discovered);
    }

    public async ValueTask<AgentRoleDefaultUpdateResult> UpdateAsync(
        AgentRoleDefaultUpdate request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            !Enum.IsDefined(request.Role) ||
            request.Provider is null ||
            string.IsNullOrWhiteSpace(request.Provider.Value) ||
            request.Model is null ||
            string.IsNullOrWhiteSpace(request.Model.Value))
        {
            return DefaultFailure(
                "invalid_agent_default",
                "A role, provider, and model are required.");
        }

        if (!providers.TryGetValue(request.Provider.Value, out GoalModelProviderRegistration? provider))
        {
            return DefaultFailure(
                "provider_missing",
                $"Provider '{request.Provider.Value}' is not configured.");
        }

        ModelDiscoverySnapshot discovered = await DiscoverModelsAsync(
            forceRefresh: false,
            cancellationToken);
        GoalModelCandidate? model = FindModel(discovered, request.Provider, request.Model);
        if (model is null)
        {
            return DefaultFailure(
                "model_unavailable",
                $"Chat model '{request.Model.Value}' is unavailable from '{provider.Name.Value}'.");
        }


        if (!model.SupportedRoles.Contains(request.Role))
        {
            return DefaultFailure(
                "model_role_unsupported",
                $"Model '{provider.Name.Value}/{request.Model.Value}' does not fully support {request.Role}; " +
                $"required capabilities: {string.Join(", ", AgentRoleModelPolicy.RequiredCapabilities(request.Role))}.");
        }

        StoredAgentRoleDefault stored = await defaultStore.SaveAsync(new(
            MapRole(request.Role),
            new(provider.Name.Value),
            new(model.Model.Value),
            timeProvider.GetUtcNow()), cancellationToken);
        return new(MapDefault(stored), ErrorCode: null, Error: null);
    }

    private async ValueTask<StoredGoal?> GetActiveGoalAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        if (goalId is null || string.IsNullOrWhiteSpace(goalId.Value))
        {
            return null;
        }

        StoredGoal? goal = await goalStore.GetAsync(goalId.Value, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        return goal is not null && workspace?.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal) is true
            ? goal
            : null;
    }

    private GoalModelProviderRegistration Provider(string name) =>
        providers.TryGetValue(name, out GoalModelProviderRegistration? provider)
            ? provider
            : throw new InvalidDataException($"Stored model provider '{name}' is not configured.");

    private async ValueTask<AgentRoleDefault> DefaultAsync(
        AgentRole role,
        CancellationToken cancellationToken) =>
        (await DefaultsAsync(cancellationToken))[role];

    private async ValueTask<IReadOnlyDictionary<AgentRole, AgentRoleDefault>> DefaultsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<AgentRole, StoredAgentRoleDefault> stored =
            (await defaultStore.ListAsync(cancellationToken))
            .ToDictionary(item => MapRole(item.Role));
        return Enum.GetValues<AgentRole>().ToDictionary(role => role, role =>
        {
            if (stored.TryGetValue(role, out StoredAgentRoleDefault? value) &&
                providers.ContainsKey(value.Provider.Value))
            {
                return MapDefault(value);
            }

            GoalModelProviderRegistration provider = Provider(defaultRoutes[role].Value);
            return new(
                role,
                provider.Name,
                provider.DefaultModel,
                provider.Access,
                IsPersisted: false,
                UpdatedAt: null);
        });
    }

    private AgentRoleDefault MapDefault(StoredAgentRoleDefault value)
    {
        AgentRole role = MapRole(value.Role);
        GoalModelProviderRegistration provider = Provider(value.Provider.Value);
        return new(
            role,
            provider.Name,
            new(value.Model.Value),
            provider.Access,
            IsPersisted: true,
            value.UpdatedAt);
    }

    private async ValueTask<ModelDiscoverySnapshot> DiscoverModelsAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ModelDiscoverySnapshot? current = discovery;
        if (!forceRefresh && current is not null)
        {
            return current;
        }

        List<GoalModelCandidate> models = [];
        List<GoalModelProviderIssue> issues = [];
        foreach (GoalModelProviderRegistration provider in providers.Values
                     .OrderBy(item => item.Access)
                     .ThenBy(item => item.Name.Value, StringComparer.OrdinalIgnoreCase))
        {
            ModelCatalog catalog = await provider.Provider.GetModelsAsync(cancellationToken);
            models.AddRange(catalog.Models
                .Where(model => model.Purposes?.Contains(ModelPurpose.Chat) is true)
                .Select(model => Map(provider, model)));
            if (catalog.Error is not null)
            {
                issues.Add(new(
                    provider.Name,
                    catalog.Error.Code,
                    catalog.Error.Message,
                    catalog.Error.IsTransient));
            }
        }

        ModelDiscoverySnapshot refreshed = new(
            models.OrderBy(model => model.Access)
                .ThenBy(model => model.Provider.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.Model.Value, StringComparer.Ordinal)
                .ToArray(),
            issues);
        discovery = refreshed;
        return refreshed;
    }

    private AgentDefaultsSnapshot Snapshot(
        IReadOnlyList<AgentRoleDefault> defaults,
        ModelDiscoverySnapshot discovered) => new(
        defaults,
        discovered.Models,
        discovered.Issues,
        ProviderStatuses(discovered),
        ValidateDefaults(defaults, discovered));

    private IReadOnlyList<AgentModelProviderStatus> UndiscoveredProviders() => providers.Values
        .OrderBy(provider => provider.Access)
        .ThenBy(provider => provider.Name.Value, StringComparer.OrdinalIgnoreCase)
        .Select(provider => new AgentModelProviderStatus(
            provider.Name,
            provider.Access,
            provider.DefaultModel,
            DiscoveredChatModels: 0,
            RoleCompatibleModels: 0,
            HasPublishedPricing: false,
            AgentModelProviderAvailability.Empty,
            "Catalog discovery has not run."))
        .ToArray();

    private IReadOnlyList<AgentModelProviderStatus> ProviderStatuses(
        ModelDiscoverySnapshot discovered) => providers.Values
        .OrderBy(provider => provider.Access)
        .ThenBy(provider => provider.Name.Value, StringComparer.OrdinalIgnoreCase)
        .Select(provider =>
        {
            GoalModelCandidate[] models = discovered.Models
                .Where(model => model.Provider == provider.Name)
                .ToArray();
            GoalModelProviderIssue? issue = discovered.Issues.FirstOrDefault(candidate =>
                candidate.Provider == provider.Name);
            AgentModelProviderAvailability availability = (models.Length, issue) switch
            {
                ( > 0, not null) => AgentModelProviderAvailability.Degraded,
                ( > 0, null) => AgentModelProviderAvailability.Available,
                (0, not null) => AgentModelProviderAvailability.Unavailable,
                _ => AgentModelProviderAvailability.Empty,
            };
            return new AgentModelProviderStatus(
                provider.Name,
                provider.Access,
                provider.DefaultModel,
                models.Length,
                models.Count(model => model.SupportedRoles.Count > 0),
                models.Any(model => model.InputPrice is not null && model.OutputPrice is not null),
                availability,
                issue?.Message ?? (models.Length == 0 ? "No chat models were discovered." : null));
        })
        .ToArray();

    private static IReadOnlyList<AgentRoleDefaultIssue> ValidateDefaults(
        IReadOnlyList<AgentRoleDefault> defaults,
        ModelDiscoverySnapshot discovered) => defaults.Select(roleDefault =>
        {
            GoalModelCandidate? model = FindModel(
                discovered,
                roleDefault.Provider,
                roleDefault.Model);
            if (model is null)
            {
                GoalModelProviderIssue? providerIssue = discovered.Issues.FirstOrDefault(issue =>
                    issue.Provider == roleDefault.Provider);
                return new AgentRoleDefaultIssue(
                    roleDefault.Role,
                    roleDefault.Provider,
                    roleDefault.Model,
                    new(providerIssue is null
                        ? "default_model_unavailable"
                        : "default_provider_unavailable"),
                    providerIssue?.Message ??
                    $"Configured model '{roleDefault.Provider.Value}/{roleDefault.Model.Value}' was not discovered.");
            }

            return model.SupportedRoles.Contains(roleDefault.Role)
                ? null
                : new AgentRoleDefaultIssue(
                    roleDefault.Role,
                    roleDefault.Provider,
                    roleDefault.Model,
                    new("default_model_role_unsupported"),
                    $"Configured model does not fully support {roleDefault.Role}; required capabilities: " +
                    $"{string.Join(", ", AgentRoleModelPolicy.RequiredCapabilities(roleDefault.Role))}.");
        })
        .OfType<AgentRoleDefaultIssue>()
        .ToArray();

    private static GoalModelCandidate? FindModel(
        ModelDiscoverySnapshot discovered,
        ModelProviderName provider,
        AgentModel model) => discovered.Models.FirstOrDefault(candidate =>
        candidate.Provider == provider && candidate.Model == model);

    private static AgentRole ParseRole(string value) =>
        Enum.TryParse(value, ignoreCase: false, out AgentRole parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Stored agent role '{value}' is invalid.");

    private static AgentDefaultRole MapRole(AgentRole role) => role switch
    {
        AgentRole.Lead => AgentDefaultRole.Lead,
        AgentRole.Implementer => AgentDefaultRole.Implementer,
        AgentRole.Reviewer => AgentDefaultRole.Reviewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static AgentRole MapRole(AgentDefaultRole role) => role switch
    {
        AgentDefaultRole.Lead => AgentRole.Lead,
        AgentDefaultRole.Implementer => AgentRole.Implementer,
        AgentDefaultRole.Reviewer => AgentRole.Reviewer,
        _ => throw new InvalidDataException($"Stored agent role '{role}' is invalid."),
    };

    private static GoalModelCandidate Map(
        GoalModelProviderRegistration provider,
        ModelDescriptor model) => new(
        provider.Name,
        new(model.Id),
        provider.Access,
        model.Capabilities.Select(capability => new ModelCapability(capability)).ToArray(),
        AgentRoleModelPolicy.SupportedRoles(model.Capabilities),
        model.ContextLength is null ? null : new(model.ContextLength.Value),
        model.Pricing is null ? null : new(model.Pricing.InputUsdPerToken * 1_000_000m),
        model.Pricing is null ? null : new(model.Pricing.OutputUsdPerToken * 1_000_000m),
        model.Pricing is null ? null : new(model.Pricing.UsdPerRequest));

    private static GoalModelSelectionResult Failure(string code, string error) =>
        new(null, code, error);

    private static AgentRoleDefaultUpdateResult DefaultFailure(string code, string error) =>
        new(null, code, error);

    private static GoalModelRouteResult RouteFailure(string code, string error) =>
        new(null, new(code), new(error));

    private sealed record ModelDiscoverySnapshot(
        IReadOnlyList<GoalModelCandidate> Models,
        IReadOnlyList<GoalModelProviderIssue> Issues);
}
