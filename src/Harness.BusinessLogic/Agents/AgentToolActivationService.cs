using System.Text.Json;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Tools;

namespace Harness.BusinessLogic.Agents;

public sealed record AgentToolsetRequestResult(
    AgentToolModuleId Module,
    bool IsGrantedForNextTurn,
    string? ErrorCode,
    string? Error);

internal interface IAgentToolActivationService
{
    IReadOnlySet<string> Consume(GoalId goalId, AgentRole role);
    ValueTask<AgentToolsetRequestResult> RequestAsync(
        GoalId goalId, AgentRole role, AgentToolModuleId module,
        CancellationToken cancellationToken = default);
}

internal sealed class AgentToolActivationService(
    IToolEvidenceStore evidenceStore,
    IAgentToolExposureConfigurationStore exposureStore,
    TimeProvider timeProvider) : IAgentToolActivationService
{
    private readonly object gate = new();
    private readonly Dictionary<string, HashSet<string>> pending = new(StringComparer.Ordinal);

    public IReadOnlySet<string> Consume(GoalId goalId, AgentRole role)
    {
        string key = Key(goalId, role);
        lock (gate)
        {
            pending.Remove(key, out HashSet<string>? modules);
            modules ??= new(StringComparer.Ordinal);
            modules.UnionWith(exposureStore.Current.DirectModuleIds);
            return modules;
        }
    }

    public async ValueTask<AgentToolsetRequestResult> RequestAsync(
        GoalId goalId, AgentRole role, AgentToolModuleId module,
        CancellationToken cancellationToken = default)
    {
        AgentToolModule? candidate = AgentToolCatalog.Default.Modules.FirstOrDefault(item =>
            item.Id == module && item.Availability is AgentToolModuleAvailability.Available &&
            item.Exposure is AgentToolExposure.OnDemand && item.Roles.Contains(role));
        if (candidate is null)
            return new(module, false, "toolset_unavailable",
                "The named toolset is unavailable for this role or is not on demand.");
        lock (gate)
        {
            string key = Key(goalId, role);
            if (!pending.TryGetValue(key, out HashSet<string>? modules))
                pending[key] = modules = new(StringComparer.Ordinal);
            modules.Add(module.Value);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        StoredToolCall call = new(new(Guid.NewGuid().ToString("N")), goalId.Value,
            new($"toolset-{Guid.NewGuid():N}"), DataAccess.Evidence.ToolKind.ToolsetGrant,
            JsonSerializer.Serialize(new { role, module = module.Value, boundary = "next-role-turn" }),
            ToolCallState.Running, null, now, null);
        StoredToolCallStart started = await evidenceStore.StartAsync(call, cancellationToken);
        await evidenceStore.CompleteAsync(started.ToolCall.Id, ToolCallState.Running,
            ToolCallState.Succeeded, JsonSerializer.Serialize(new { granted = true, expires = "after-next-role-turn" }),
            now, cancellationToken);
        return new(module, true, null, null);
    }

    private static string Key(GoalId goalId, AgentRole role) => $"{goalId.Value}:{role}";
}
