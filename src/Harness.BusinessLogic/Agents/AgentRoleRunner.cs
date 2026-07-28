using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Agents;

internal sealed class AgentRoleRunner : IAgentRoleRunner
{
    private const int MaximumTaskCharacters = 64 * 1024;
    private readonly IGoalModelRouteResolver routeResolver;
    private readonly ILoggerFactory loggerFactory;

    public AgentRoleRunner(
        IGoalModelRouteResolver routeResolver,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(routeResolver);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        this.routeResolver = routeResolver;
        this.loggerFactory = loggerFactory;
    }

    public async ValueTask<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.GoalId is null ||
            string.IsNullOrWhiteSpace(request.GoalId.Value) ||
            request.Task is null ||
            string.IsNullOrWhiteSpace(request.Task.Value) ||
            request.Task.Value.Length > MaximumTaskCharacters ||
            !Enum.IsDefined(request.Role) ||
            request.MaximumOutputTokens?.Value is <= 0)
        {
            return new(
                request?.Role ?? AgentRole.Lead,
                Output: null,
                new("invalid_agent_request"),
                new("An agent role and a task of at most 65536 characters are required."));
        }

        try
        {
            GoalModelRouteResult resolved = await routeResolver.ResolveAsync(
                request.GoalId,
                request.Role,
                cancellationToken);
            if (resolved.Route is null)
            {
                return new(request.Role, Output: null, resolved.ErrorCode, resolved.Error);
            }

            GoalModelRoute route = resolved.Route;
            if (route.Access is ModelAccess.Remote && request.MaximumOutputTokens is null)
            {
                return new(
                    request.Role,
                    Output: null,
                    new("maximum_output_tokens_required"),
                    new("Remote agent execution requires a positive output-token maximum."));
            }

            AIAgent agent = new ChatClientAgent(
                new ModelProviderChatClient(
                    route.Provider,
                    route.Model,
                    route.Access is ModelAccess.Remote ? route.GoalId : null,
                    route.Role,
                    request.MaximumOutputTokens),
                Instructions(request.Role),
                Name(request.Role),
                Description(request.Role),
                tools: [],
                loggerFactory,
                services: null);
            AgentSession session = await agent.CreateSessionAsync(cancellationToken);
            AgentResponse response = await agent.RunAsync(
                request.Task.Value.Trim(),
                session,
                cancellationToken: cancellationToken);
            return new(request.Role, new(response.Text), ErrorCode: null, Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(
                request.Role,
                Output: null,
                new("agent_run_failed"),
                new(exception.Message));
        }
    }

    private static string Name(AgentRole role) => role switch
    {
        AgentRole.Lead => "lead",
        AgentRole.Implementer => "implementer",
        AgentRole.Reviewer => "reviewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static string Description(AgentRole role) => role switch
    {
        AgentRole.Lead => "Plans bounded work and coordinates specialist roles.",
        AgentRole.Implementer => "Implements an explicitly bounded task within the accepted architecture.",
        AgentRole.Reviewer => "Reviews evidence and identifies correctness, safety, and regression risks.",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static string Instructions(AgentRole role) => role switch
    {
        AgentRole.Lead =>
            "You are the lead agent. Turn the supplied objective into bounded, verifiable work. " +
            "Respect accepted architecture and do not claim completion without evidence.",
        AgentRole.Implementer =>
            "You are the implementer agent. Complete only the supplied bounded task. " +
            "Keep changes narrow, follow accepted architecture, and report verification evidence.",
        AgentRole.Reviewer =>
            "You are the reviewer agent. Review the supplied work independently. " +
            "Prioritize correctness, regressions, boundary violations, missing tests, and unsupported claims.",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
