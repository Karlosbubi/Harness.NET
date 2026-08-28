using System.Reflection;
using System.Text.Json;
using Harness.BusinessLogic.Goals;
using Microsoft.Extensions.AI;

namespace Harness.BusinessLogic.Agents;

internal sealed class ObservedAgentFunction(
    AIFunction inner,
    AgentActivityService activityService,
    GoalId goalId,
    AgentRole role) : AIFunction
{
    public override string Name => inner.Name;

    public override string Description => inner.Description;

    public override JsonElement JsonSchema => inner.JsonSchema;

    public override JsonElement? ReturnJsonSchema => inner.ReturnJsonSchema;

    public override JsonSerializerOptions JsonSerializerOptions => inner.JsonSerializerOptions;

    public override MethodInfo? UnderlyingMethod => inner.UnderlyingMethod;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        using AgentActivityLease activity = activityService.BeginTool(goalId, role, Name);
        try
        {
            object? result = await inner.InvokeAsync(arguments, cancellationToken);
            activity.Complete();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity.Cancel();
            throw;
        }
        catch (Exception)
        {
            activity.Fail();
            throw;
        }
    }
}
