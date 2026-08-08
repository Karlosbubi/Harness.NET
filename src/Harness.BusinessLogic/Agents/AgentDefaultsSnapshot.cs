namespace Harness.BusinessLogic.Agents;

public sealed record AgentDefaultsSnapshot(
    IReadOnlyList<AgentRoleDefault> Roles,
    IReadOnlyList<GoalModelCandidate> Models,
    IReadOnlyList<GoalModelProviderIssue> Issues,
    IReadOnlyList<AgentModelProviderStatus> Providers,
    IReadOnlyList<AgentRoleDefaultIssue> DefaultIssues);
