namespace Harness.BusinessLogic.Agents;

public sealed record GoalModelProviderIssue(
    ModelProviderName Provider,
    string Code,
    string Message,
    bool IsTransient);
