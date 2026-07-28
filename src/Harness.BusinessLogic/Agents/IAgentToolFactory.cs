using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal interface IAgentToolFactory
{
    IList<AITool> Create(
        AgentRole role,
        Goals.GoalId goalId,
        IReadOnlyList<AgentFileArea> fileAreas);
}
