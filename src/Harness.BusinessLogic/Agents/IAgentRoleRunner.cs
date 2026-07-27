namespace Harness.BusinessLogic.Agents;

public interface IAgentRoleRunner
{
    ValueTask<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);
}
