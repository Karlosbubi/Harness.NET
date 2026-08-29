using System.Globalization;
using System.Text.Json;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Mcp;

namespace Harness.BusinessLogic.Mcp;

internal sealed partial class InboundMcpApplicationService
{
    public async ValueTask<InboundMcpApplicationResult> CreateGoalAsync(
        InboundMcpCallContext context,
        InboundMcpGoalCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        if (!workspace.Id.Equals(request.WorkspaceId, StringComparison.Ordinal))
            return Failure("stale_workspace",
                "The supplied workspace identity is not the active trusted workspace.");
        GoalResult result = await goalService.CreateAsync(new(
            request.WorkspaceId,
            request.Title,
            request.Objective,
            new(request.ReviewCycleLimit),
            request.RemoteBudgetMicrousd is null
                ? null
                : new(request.RemoteBudgetMicrousd.Value)), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_creation_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> UpdateGoalSettingsAsync(
        InboundMcpCallContext context,
        InboundMcpGoalSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        GoalResult result = await goalService.UpdateSettingsAsync(new(
            goal!.Id,
            new(request.ReviewCycleLimit),
            request.RemoteBudgetMicrousd is null
                ? null
                : new(request.RemoteBudgetMicrousd.Value),
            request.ExpectedUpdatedAt), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_configuration_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> ExtendGoalBudgetAsync(
        InboundMcpCallContext context,
        InboundMcpGoalBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        GoalBudgetExtensionResult result = await goalService.ExtendRemoteBudgetAsync(new(
            goal!.Id,
            request.ExpectedBudgetMicrousd is null
                ? null
                : new(request.ExpectedBudgetMicrousd.Value),
            new(request.NewBudgetMicrousd),
            new(request.Reason)), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_budget_extension_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> DiscoverGoalModelsAsync(
        InboundMcpCallContext context,
        InboundMcpGoalCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        if (!TryPage(request.MaximumResults, request.Continuation, out int offset,
                out InboundMcpApplicationResult? pageFailure))
            return pageFailure!;
        AgentRole? role = null;
        if (request.Role is not null)
        {
            if (!Enum.TryParse(request.Role, true, out AgentRole parsedRole))
                return Failure("invalid_goal_role", "Role must be Lead, Implementer, or Reviewer.");
            role = parsedRole;
        }
        GoalModelCatalog result = await goalModelService.DiscoverAsync(
            goal!.Id, cancellationToken);
        string search = request.Search?.Trim() ?? string.Empty;
        GoalModelCandidate[] matchingModels = result.Models
            .Where(model => request.Provider is null ||
                model.Provider.Value.Equals(request.Provider, StringComparison.OrdinalIgnoreCase))
            .Where(model => role is null || model.SupportedRoles.Contains(role.Value))
            .Where(model => search.Length == 0 ||
                model.Provider.Value.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                model.Model.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(model => model.Provider.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Model.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        GoalModelCandidate[] models = matchingModels
            .Skip(offset)
            .Take(request.MaximumResults)
            .ToArray();
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result = new
                {
                    result.GoalId,
                    Models = models,
                    result.Issues,
                    totalMatches = matchingModels.Length,
                    continuation = NextContinuation(
                        offset, models.Length, matchingModels.Length),
                },
                selections = await goalModelService.GetSelectionsAsync(
                    goal.Id, cancellationToken),
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_model_discovery_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> SelectGoalModelAsync(
        InboundMcpCallContext context,
        InboundMcpGoalModelRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        if (!Enum.TryParse(request.Role, true, out AgentRole role))
            return Failure("invalid_goal_role", "Role must be Lead, Implementer, or Reviewer.");
        GoalModelSelectionResult result = await goalModelService.SelectAsync(new(
            goal!.Id,
            role,
            new(request.Provider),
            new(request.Model)), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_model_selection_failed", result.Error);
    }

    public ValueTask<InboundMcpApplicationResult> StartGoalPlanningAsync(
        InboundMcpCallContext context,
        InboundMcpGoalRequest request,
        CancellationToken cancellationToken = default) => StartGoalOperationAsync(
            context,
            request.GoalId,
            "planning",
            goalId => token => workflowService.StartPlanningAsync(new(goalId), token),
            cancellationToken);

    public ValueTask<InboundMcpApplicationResult> ResumeGoalAsync(
        InboundMcpCallContext context,
        InboundMcpGoalRequest request,
        CancellationToken cancellationToken = default) => StartGoalOperationAsync(
            context,
            request.GoalId,
            "resume",
            goalId => token => workflowService.ResumeAsync(new(goalId), token),
            cancellationToken);

    public async ValueTask<InboundMcpApplicationResult> RetryGoalAsync(
        InboundMcpCallContext context,
        InboundMcpGoalRetryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse(request.Role, true, out GoalWorkflowRetryRole role))
            return Failure("invalid_retry_role", "Role must be Lead, Implementer, or Reviewer.");
        return await StartGoalOperationAsync(
            context,
            request.GoalId,
            $"retry-{role}",
            goalId => token => workflowService.RetryAsync(new(
                goalId,
                role,
                string.IsNullOrWhiteSpace(request.Guidance)
                    ? null
                    : new(request.Guidance)), token),
            cancellationToken);
    }

    public async ValueTask<InboundMcpApplicationResult> CancelGoalOperationAsync(
        InboundMcpCallContext context,
        InboundMcpGoalOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        InboundGoalOperationResult result = await goalOperations.CancelAsync(
            goal!.Id, new(request.OperationId), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_operation_cancel_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> AbortGoalAsync(
        InboundMcpCallContext context,
        InboundMcpGoalAbortRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        if (goalOperations.Get(goal!.Id) is { State: InboundGoalOperationState.Running })
            return Failure("goal_operation_active",
                "Cancel the exact active inbound operation before aborting this goal.");
        GoalWorkflowSnapshot result = await workflowService.AbortAsync(new(
            goal.Id, new(request.Reason)), cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace!),
            result,
            freshness = context.RequestedAt,
        });
    }

    public async ValueTask<InboundMcpApplicationResult> DecidePlanAsync(
        InboundMcpCallContext context, InboundMcpPlanDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        GoalView? goal = await goalService.GetAsync(new(request.GoalId), cancellationToken);
        if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            return Failure("goal_unavailable", "The goal is not part of the active workspace.");
        if (!Enum.TryParse(request.Decision, true, out PlanDecision decision))
            return Failure("invalid_plan_decision", "Decision must be Approve or Deny.");
        PlanResult result = await goalService.DecidePlanAsync(new(new(request.GoalId),
            new(request.PlanId), decision, request.Reason), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace),
                result,
                freshness = context.RequestedAt
            })
            : Failure(result.ErrorCode ?? "plan_decision_failed", result.Error);
    }

    public ValueTask<InboundMcpApplicationResult> BuildAsync(
        InboundMcpCallContext context, InboundMcpExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(context, request, DotNetOperation.Build, cancellationToken);

    public ValueTask<InboundMcpApplicationResult> TestAsync(
        InboundMcpCallContext context, InboundMcpExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(context, request, DotNetOperation.Test, cancellationToken);

    public async ValueTask<InboundMcpApplicationResult> PreviewCommitAsync(
        InboundMcpCallContext context,
        InboundMcpGoalRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        GoalCommitPreviewResult result = await acceptanceService.PreviewAsync(
            goal!.Id, cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "commit_preview_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> RequestCommitApprovalAsync(
        InboundMcpCallContext context,
        InboundMcpCommitApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        GoalCommitApprovalResult result = await acceptanceService.RequestAsync(new(
            goal!.Id,
            new(request.RunId),
            new(request.ExpectedHead),
            new(request.ExpectedDiffHash),
            new(request.Message),
            new(request.AuthorName),
            new(request.AuthorEmail)), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "commit_approval_failed", result.Error);
    }

    public async ValueTask<InboundMcpApplicationResult> DecideCommitAsync(
        InboundMcpCallContext context,
        InboundMcpCommitDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, request.GoalId, cancellationToken);
        if (failure is not null) return failure;
        if (!Enum.TryParse(request.Decision, true, out GoalCommitDecision decision))
            return Failure("invalid_commit_decision", "Decision must be Approve or Deny.");
        GoalCommitApprovalView? approval = await acceptanceService.GetAsync(
            goal!.Id, new(request.RunId), cancellationToken);
        if (approval is null ||
            !approval.Id.Value.Equals(request.ApprovalId, StringComparison.Ordinal))
            return Failure("commit_approval_missing",
                "The approval does not match the active workspace, exact goal, and run.");
        GoalCommitApprovalResult result = await acceptanceService.DecideAsync(new(
            approval.Id,
            decision,
            string.IsNullOrWhiteSpace(request.Reason)
                ? null
                : new(request.Reason)), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "commit_decision_failed", result.Error);
    }

    private async ValueTask<InboundMcpApplicationResult> ExecuteAsync(
        InboundMcpCallContext context, InboundMcpExecutionRequest request,
        DotNetOperation operation, CancellationToken cancellationToken)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        GoalView? goal = await goalService.GetAsync(new(request.GoalId), cancellationToken);
        if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            return Failure("goal_unavailable", "The goal is not part of the active workspace.");
        DotNetOperationView result = await mutationService.RunDotNetAsync(new(
            request.GoalId, new ToolCorrelationId(request.CorrelationId), operation), cancellationToken);
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace),
                goal.Id,
                operation,
                result,
                freshness = context.RequestedAt
            })
            : Failure(result.ErrorCode ?? "execution_failed", result.Error);
    }

}
