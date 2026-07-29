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
    private const int MaximumOutputTokens = 8192;
    private readonly IGoalStore goalStore;
    private readonly IWorkspaceStore workspaceStore;
    private readonly IGoalModelSelectionStore selectionStore;
    private readonly IAgentRoleDefaultStore defaultStore;
    private readonly IReadOnlyDictionary<string, GoalModelProviderRegistration> providers;
    private readonly IReadOnlyDictionary<AgentRole, ModelProviderName> defaultRoutes;
    private readonly AgentDefaultsOptions defaultOptions;
    private readonly TimeProvider timeProvider;

    internal GoalModelService(
        IGoalStore goalStore,
        IWorkspaceStore workspaceStore,
        IGoalModelSelectionStore selectionStore,
        IAgentRoleDefaultStore defaultStore,
        IReadOnlyList<GoalModelProviderRegistration> providers,
        IReadOnlyDictionary<AgentRole, ModelProviderName> defaultRoutes,
        AgentDefaultsOptions defaultOptions,
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
        this.defaultOptions = defaultOptions;
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

        (IReadOnlyList<GoalModelCandidate> models, IReadOnlyList<GoalModelProviderIssue> issues) =
            await DiscoverModelsAsync(cancellationToken);

        return new(
            goalId,
            models,
            issues,
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
                "Select a positive remote budget when creating the goal before authorizing a remote model.");
        }

        ModelCatalog catalog = await provider.Provider.GetModelsAsync(cancellationToken);
        if (catalog.Error is not null)
        {
            return Failure(catalog.Error.Code, catalog.Error.Message);
        }

        ModelDescriptor? model = catalog.Models.FirstOrDefault(candidate =>
            candidate.Purposes?.Contains(ModelPurpose.Chat) is true &&
            candidate.Id.Equals(request.Model.Value, StringComparison.Ordinal));
        if (model is null)
        {
            return Failure(
                "model_unavailable",
                $"Chat model '{request.Model.Value}' is unavailable from '{provider.Name.Value}'.");
        }

        StoredGoalModelSelection stored = await selectionStore.SaveAsync(new(
            goal.Id,
            request.Role.ToString(),
            provider.Name.Value,
            model.Id,
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
                "Remote agent execution requires a positive goal cost cap.");
        }

        return new(
            new(goalId, role, provider.Name, model, provider.Access, provider.Provider),
            ErrorCode: null,
            Error: null);
    }

    public async ValueTask<AgentDefaultsSnapshot> GetAsync(
        CancellationToken cancellationToken = default) =>
        new((await DefaultsAsync(cancellationToken)).Values
                .OrderBy(item => item.Role)
                .ToArray(),
            Models: [],
            Issues: []);

    public async ValueTask<AgentDefaultsSnapshot> DiscoverAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<GoalModelCandidate> models, IReadOnlyList<GoalModelProviderIssue> issues) =
            await DiscoverModelsAsync(cancellationToken);
        return new(
            (await DefaultsAsync(cancellationToken)).Values
                .OrderBy(item => item.Role)
                .ToArray(),
            models,
            issues);
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
            string.IsNullOrWhiteSpace(request.Model.Value) ||
            request.MaximumOutputTokens is null ||
            request.MaximumOutputTokens.Value is < 1 or > MaximumOutputTokens)
        {
            return DefaultFailure(
                "invalid_agent_default",
                $"A role, provider, model, and output maximum of 1-{MaximumOutputTokens} are required.");
        }

        if (!providers.TryGetValue(request.Provider.Value, out GoalModelProviderRegistration? provider))
        {
            return DefaultFailure(
                "provider_missing",
                $"Provider '{request.Provider.Value}' is not configured.");
        }

        ModelCatalog catalog = await provider.Provider.GetModelsAsync(cancellationToken);
        if (catalog.Error is not null)
        {
            return DefaultFailure(catalog.Error.Code, catalog.Error.Message);
        }

        ModelDescriptor? model = catalog.Models.FirstOrDefault(candidate =>
            candidate.Purposes?.Contains(ModelPurpose.Chat) is true &&
            candidate.Id.Equals(request.Model.Value, StringComparison.Ordinal));
        if (model is null)
        {
            return DefaultFailure(
                "model_unavailable",
                $"Chat model '{request.Model.Value}' is unavailable from '{provider.Name.Value}'.");
        }

        StoredAgentRoleDefault stored = await defaultStore.SaveAsync(new(
            MapRole(request.Role),
            new(provider.Name.Value),
            new(model.Id),
            new(request.MaximumOutputTokens.Value),
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
                defaultOptions.FallbackMaximumOutputTokens,
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
            new(value.MaximumOutputTokens.Value),
            IsPersisted: true,
            value.UpdatedAt);
    }

    private async ValueTask<(
        IReadOnlyList<GoalModelCandidate> Models,
        IReadOnlyList<GoalModelProviderIssue> Issues)> DiscoverModelsAsync(
        CancellationToken cancellationToken)
    {
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

        return (
            models.OrderBy(model => model.Access)
                .ThenBy(model => model.Provider.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.Model.Value, StringComparer.Ordinal)
                .ToArray(),
            issues);
    }

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
}
