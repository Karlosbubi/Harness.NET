namespace Harness.BusinessLogic.Mutations;

public sealed record DotNetOperationRequest(
    string GoalId,
    string CorrelationId,
    string Operation);
