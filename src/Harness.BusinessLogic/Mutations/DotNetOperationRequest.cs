using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public sealed record DotNetOperationRequest(
    string GoalId,
    ToolCorrelationId CorrelationId,
    DotNetOperation Operation);
