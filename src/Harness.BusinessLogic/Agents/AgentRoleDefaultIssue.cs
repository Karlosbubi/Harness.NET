namespace Harness.BusinessLogic.Agents;

public sealed record AgentRoleDefaultIssue(
    AgentRole Role,
    ModelProviderName Provider,
    AgentModel Model,
    AgentRoleDefaultIssueCode Code,
    string Message);
