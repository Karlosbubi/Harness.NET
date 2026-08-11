using System.Text.Json;
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

public sealed record InboundMcpApplicationEnvironment(bool IsIsolatedEvaluation);

internal sealed class InboundMcpApplicationService(
    IWorkspaceService workspaceService,
    IWorkspaceAdvancedInspector advancedInspector,
    IWorkspaceGitInspector gitInspector,
    IWorkspaceDotNetInspector dotNetInspector,
    IGoalService goalService,
    IGoalWorkflowService workflowService,
    IRemoteCostService remoteCostService,
    IToolEvidenceService evidenceService,
    IWorkspaceMutationService mutationService,
    IInboundMcpUiBridge uiBridge,
    IVisualCaptureService visualCaptureService,
    IGoalCodeIntelligenceService codeIntelligenceService,
    IAgentDefaultsService agentDefaultsService,
    IInboundMcpEvaluationFixture evaluationFixture,
    InboundMcpApplicationEnvironment environment) : IInboundMcpApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<InboundMcpToolPolicy> ToolPolicies { get; } =
    [
        Read("harness_application"), Read("harness_workspace"), Read("harness_tree"),
        Read("harness_read_range"), Read("harness_git"), Read("harness_project_graph"),
        Read("harness_goals"), Read("harness_evidence"), Read("harness_ui", sensitive: true),
        Read("harness_audit", sensitive: true), Read("harness_code_problems"),
        Read("harness_code_symbol"), Read("harness_code_definition"),
        Read("harness_code_references"), Read("harness_code_implementations"),
        Read("harness_inspect_capture", sensitive: true),
        Read("harness_evaluation_snapshot", sensitive: true),
        Action("harness_decide_plan", idempotent: true),
        Action("harness_open_document", idempotent: true),
        Action("harness_ui_activate", sensitive: true, idempotent: true),
        Action("harness_request_capture", sensitive: true),
        Action("harness_build", execution: true), Action("harness_test", execution: true),
        Action("harness_evaluation_reset", sensitive: true, destructive: true,
            idempotent: true),
    ];

    public async ValueTask<InboundMcpApplicationResult> GetApplicationAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        AgentDefaultsSnapshot providers = await agentDefaultsService.GetAsync(cancellationToken);
        return Success(new
        {
            application = "Harness.NET",
            instanceId = context.InstanceId.Value,
            clientId = context.ClientId.Value,
            mode = context.Mode.ToString(),
            isolated = environment.IsIsolatedEvaluation,
            requestedAt = context.RequestedAt,
            protocol = "stateless-streamable-http",
            providers = providers.Providers,
            tools = ToolPolicies,
        });
    }

    public async ValueTask<InboundMcpApplicationResult> GetEvaluationSnapshotAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode is not InboundMcpMode.IsolatedEvaluation || !environment.IsIsolatedEvaluation)
            return Failure("evaluation_isolation_required",
                "Evaluation snapshots require a dedicated isolated evaluation process.");
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            snapshot = await evaluationFixture.SnapshotAsync(cancellationToken)
        });
    }

    public async ValueTask<InboundMcpApplicationResult> ResetEvaluationAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode is not InboundMcpMode.IsolatedEvaluation || !environment.IsIsolatedEvaluation)
            return Failure("evaluation_isolation_required",
                "Evaluation reset requires a dedicated isolated evaluation process.");
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            snapshot = await evaluationFixture.ResetAsync(cancellationToken)
        });
    }

    public async ValueTask<InboundMcpApplicationResult> GetWorkspaceAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        InboundMcpApplicationResult? modeFailure = CheckMode(context);
        if (modeFailure is not null) return modeFailure;
        WorkspaceView? workspace = await workspaceService.GetActiveAsync(cancellationToken);
        return workspace is null ? Failure("workspace_unavailable", "No workspace is active.") :
            Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace),
                workspace,
                freshness = context.RequestedAt,
            });
    }

    public async ValueTask<InboundMcpApplicationResult> ListTreeAsync(
        InboundMcpCallContext context, InboundMcpTreeRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        WorkspaceTreeResult result = await advancedInspector.ListTreeAsync(workspace.RootPath,
            new(new(request.RelativeRoot), string.IsNullOrWhiteSpace(request.Glob) ? null : new(request.Glob),
                request.MaximumDepth, request.MaximumResults,
                string.IsNullOrWhiteSpace(request.Continuation) ? null : new(request.Continuation)),
            cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            workspace.Id,
            workspace.Branch,
            result,
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> ReadRangeAsync(
        InboundMcpCallContext context, InboundMcpRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        WorkspaceRangeResult result = await advancedInspector.ReadRangeAsync(workspace.RootPath,
            new(new(request.RelativePath), request.StartLine, request.LineCount), cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            workspace.Id,
            workspace.Branch,
            documentVersion = result.Sha256,
            result,
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> GetGitAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        WorkspaceGitState result = await gitInspector.InspectAsync(workspace.RootPath, cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            workspace.Id,
            result,
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> GetProjectGraphAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        WorkspaceDotNetInfo result = await dotNetInspector.InspectAsync(
            workspace.RootPath, workspace.EntryPoint, cancellationToken);
        object[] edges = result.Projects.SelectMany(project => project.References
            .Where(reference => reference.Kind.Equals("Project", StringComparison.OrdinalIgnoreCase))
            .Select(reference => (object)new { from = project.Path, to = reference.Identity }))
            .ToArray();
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            workspace.Id,
            workspace.Branch,
            result.EntryPoint,
            result.EntryPointKind,
            result.SdkPolicy,
            result.Projects,
            edges,
            result.IsTruncated,
            result.ErrorCode,
            result.Error,
            configuration = "evaluated-default",
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> ListGoalsAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        IReadOnlyList<GoalView> goals = await goalService.ListAsync(workspace.Id, cancellationToken);
        List<object> details = [];
        foreach (GoalView goal in goals)
        {
            details.Add(new
            {
                goal,
                plan = await goalService.GetCurrentPlanAsync(goal.Id, cancellationToken),
                workflow = await workflowService.GetLatestAsync(goal.Id, cancellationToken),
                cost = await remoteCostService.GetAsync(goal.Id, cancellationToken),
            });
        }
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            workspace.Id,
            goals = details,
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> ListEvidenceAsync(
        InboundMcpCallContext context, InboundMcpGoalRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        GoalView? goal = await goalService.GetAsync(new(request.GoalId), cancellationToken);
        if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            return Failure("goal_unavailable", "The goal is not part of the active workspace.");
        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(request.GoalId, cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            goal.Id,
            evidence,
            freshness = context.RequestedAt
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

    public async ValueTask<InboundMcpApplicationResult> GetUiAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default)
    {
        InboundMcpApplicationResult? failure = CheckMode(context);
        if (failure is not null) return failure;
        bool isolated = context.Mode is InboundMcpMode.IsolatedEvaluation &&
            environment.IsIsolatedEvaluation;
        InboundUiSnapshot snapshot = await uiBridge.InspectAsync(isolated, cancellationToken);
        return Success(new { instanceId = context.InstanceId.Value, snapshot });
    }

    public async ValueTask<InboundMcpApplicationResult> ActivateUiAsync(
        InboundMcpCallContext context, InboundMcpUiActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context.Mode is not InboundMcpMode.IsolatedEvaluation || !environment.IsIsolatedEvaluation)
            return Failure("evaluation_ui_action_denied",
                "Harness UI activation is available only in isolated evaluation mode.");
        InboundUiActionResult result = await uiBridge.ActivateAsync(new(request.ActionId), cancellationToken);
        return result.WasApplied ? Success(new { instanceId = context.InstanceId.Value, result }) :
            Failure(result.ErrorCode ?? "ui_action_failed", result.Error ?? "The UI action failed.");
    }

    public async ValueTask<InboundMcpApplicationResult> OpenDocumentAsync(
        InboundMcpCallContext context, InboundMcpOpenDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        if (request.GoalId is not null)
        {
            GoalView? goal = await goalService.GetAsync(new(request.GoalId), cancellationToken);
            if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
                return Failure("goal_unavailable", "The goal is not part of the active workspace.");
        }
        InboundUiActionResult result = await uiBridge.OpenDocumentAsync(
            new(request.RelativePath, request.GoalId), cancellationToken);
        return result.WasApplied
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace),
                result,
                freshness = context.RequestedAt
            })
            : Failure(result.ErrorCode ?? "document_open_failed",
                result.Error ?? "The document could not be opened.");
    }

    public async ValueTask<InboundMcpApplicationResult> RequestCaptureAsync(
        InboundMcpCallContext context, InboundMcpCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse(request.Target, true, out VisualCaptureTarget target))
            return Failure("invalid_capture_target", "The capture target is not supported.");
        VisualCaptureResult result = await visualCaptureService.CaptureAsync(new(
            new(request.GoalId), new ToolCorrelationId(request.CorrelationId),
            VisualCaptureInitiator.Developer, new(request.RelatedAction), new("Harness.NET"),
            target, context.RequestedAt), cancellationToken);
        return result.Outcome is VisualCaptureOutcome.Succeeded
            ? Success(new { instanceId = context.InstanceId.Value, result })
            : Failure(result.ErrorCode ?? "capture_failed", result.Error ?? result.Outcome.ToString());
    }

    public async ValueTask<InboundMcpApplicationResult> InspectCaptureAsync(
        InboundMcpCallContext context, InboundMcpCaptureInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        VisualCaptureInspectionResult result = await visualCaptureService.InspectAsync(
            new(request.GoalId), new(request.CaptureId), VisualCaptureModelAccess.Local,
            cancellationToken);
        return result.Outcome is VisualCaptureOutcome.Succeeded
            ? Success(new { instanceId = context.InstanceId.Value, result })
            : Failure(result.ErrorCode ?? "capture_inspection_failed",
                result.Error ?? result.Outcome.ToString());
    }

    public async ValueTask<InboundMcpApplicationResult> InspectCodeProblemsAsync(
        InboundMcpCallContext context, InboundMcpCodeRequest request,
        CancellationToken cancellationToken = default) => Success(new
        {
            instanceId = context.InstanceId.Value,
            result = await codeIntelligenceService.InspectProblemsAsync(new(request.GoalId),
                GoalWorkspaceScope.Original, new(request.RelativePath), cancellationToken),
            freshness = context.RequestedAt,
        });

    public ValueTask<InboundMcpApplicationResult> GetCodeSymbolAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default) => CodePositionAsync(context, request,
            (goal, path, position, token) => codeIntelligenceService.GetSymbolAsync(
                goal, GoalWorkspaceScope.Original, path, position, token), cancellationToken);

    public ValueTask<InboundMcpApplicationResult> FindCodeDefinitionAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default) => CodePositionAsync(context, request,
            (goal, path, position, token) => codeIntelligenceService.FindDefinitionAsync(
                goal, GoalWorkspaceScope.Original, path, position, token), cancellationToken);

    public ValueTask<InboundMcpApplicationResult> FindCodeReferencesAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default) => CodePositionAsync(context, request,
            (goal, path, position, token) => codeIntelligenceService.FindReferencesAsync(
                goal, GoalWorkspaceScope.Original, path, position, token), cancellationToken);

    public ValueTask<InboundMcpApplicationResult> FindCodeImplementationsAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default) => CodePositionAsync(context, request,
            (goal, path, position, token) => codeIntelligenceService.FindImplementationsAsync(
                goal, GoalWorkspaceScope.Original, path, position, token), cancellationToken);

    private static async ValueTask<InboundMcpApplicationResult> CodePositionAsync<T>(
        InboundMcpCallContext context,
        InboundMcpCodePositionRequest request,
        Func<GoalId, WorkbenchCodeDocumentPath, WorkbenchCodePosition, CancellationToken,
            ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        if (request.Line < 0 || request.Character < 0)
            return Failure("invalid_code_position", "Line and character must be zero-based non-negative values.");
        T result = await operation(new(request.GoalId), new(request.RelativePath),
            new(request.Line, request.Character), cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            result,
            freshness = context.RequestedAt
        });
    }

    private async ValueTask<WorkspaceView?> TrustedWorkspaceAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken)
    {
        if (CheckMode(context) is not null) return null;
        WorkspaceView? workspace = await workspaceService.GetActiveAsync(cancellationToken);
        return workspace?.IsTrusted == true ? workspace : null;
    }

    private InboundMcpApplicationResult? CheckMode(InboundMcpCallContext context) =>
        context.Mode is InboundMcpMode.IsolatedEvaluation && !environment.IsIsolatedEvaluation
            ? Failure("evaluation_isolation_required",
                "Isolated evaluation mode requires Harness to start with a disposable evaluation root.")
            : context.Mode is InboundMcpMode.Normal && environment.IsIsolatedEvaluation
                ? Failure("normal_mode_unavailable", "An isolated evaluation process cannot expose normal state.")
                : null;

    private InboundMcpApplicationResult WorkspaceFailure(InboundMcpCallContext context) =>
        CheckMode(context) ?? Failure("workspace_unavailable", "No active trusted workspace is available.");

    private static string SourceId(WorkspaceView workspace) =>
        $"{workspace.Id}:original:{workspace.Branch}:{workspace.EntryPoint}";

    private static InboundMcpApplicationResult Success(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), false, null, null);

    private static InboundMcpApplicationResult Failure(string code, string error) =>
        new("{}", true, code, error);

    private static InboundMcpToolPolicy Read(string id, bool sensitive = false) =>
        new(new(id), true, false, false, sensitive, false, true);
    private static InboundMcpToolPolicy Action(
        string id, bool execution = false, bool sensitive = false,
        bool destructive = false, bool idempotent = false) =>
        new(new(id), false, true, execution, sensitive, destructive, idempotent);
}
