using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Evidence;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class AgentToolActivationServiceTests
{
    [Fact]
    public async Task Grant_is_recorded_and_consumed_for_exactly_one_role_turn()
    {
        EvidenceStore evidence = new();
        AgentToolActivationService service = new(evidence, new ExposureStore([]), TimeProvider.System);
        GoalId goal = new("goal-1");

        AgentToolsetRequestResult result = await service.RequestAsync(
            goal, AgentRole.Lead, new("semantic-hierarchy"));

        Assert.True(result.IsGrantedForNextTurn);
        Assert.Contains("semantic-hierarchy", service.Consume(goal, AgentRole.Lead));
        Assert.DoesNotContain("semantic-hierarchy", service.Consume(goal, AgentRole.Lead));
        Assert.DoesNotContain("semantic-hierarchy", service.Consume(goal, AgentRole.Reviewer));
        StoredToolCall call = Assert.Single(evidence.Items);
        Assert.Equal(ToolKind.ToolsetGrant, call.Tool);
        Assert.Equal(ToolCallState.Succeeded, call.State);
    }

    [Fact]
    public async Task Unavailable_or_wrong_role_module_is_not_granted()
    {
        EvidenceStore evidence = new();
        AgentToolActivationService service = new(evidence, new ExposureStore([]), TimeProvider.System);

        AgentToolsetRequestResult result = await service.RequestAsync(
            new("goal-1"), AgentRole.Reviewer, new("build-test"));

        Assert.False(result.IsGrantedForNextTurn);
        Assert.Equal("toolset_unavailable", result.ErrorCode);
        Assert.Empty(evidence.Items);
    }

    private sealed class ExposureStore(IReadOnlyList<string> values)
        : IAgentToolExposureConfigurationStore
    {
        public AgentToolExposureConfiguration Current { get; private set; } = new(values);
        public ValueTask<AgentToolExposureConfiguration> SaveAsync(
            AgentToolExposureConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Current = configuration;
            return ValueTask.FromResult(configuration);
        }
    }

    private sealed class EvidenceStore : IToolEvidenceStore
    {
        public List<StoredToolCall> Items { get; } = [];

        public ValueTask<StoredToolCallStart> StartAsync(
            StoredToolCall toolCall, CancellationToken cancellationToken = default)
        {
            Items.Add(toolCall);
            return ValueTask.FromResult(new StoredToolCallStart(toolCall, true));
        }

        public ValueTask<StoredToolCall> CompleteAsync(
            ToolCallId toolCallId, ToolCallState expectedState, ToolCallState nextState,
            string resultJson, DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            int index = Items.FindIndex(item => item.Id == toolCallId);
            StoredToolCall updated = Items[index] with
            {
                State = nextState,
                ResultJson = resultJson,
                CompletedAt = completedAt,
            };
            Items[index] = updated;
            return ValueTask.FromResult(updated);
        }

        public ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(
            string goalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<StoredToolCall>>(
                Items.Where(item => item.GoalId == goalId).ToArray());
    }
}
