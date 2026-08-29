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

public sealed record InboundMcpApplicationEnvironment(bool IsIsolatedEvaluation);

internal sealed partial class InboundMcpApplicationService(
    IWorkspaceService workspaceService,
    IWorkspaceAdvancedInspector advancedInspector,
    IWorkspaceGitInspector gitInspector,
    IDeveloperGitService developerGitService,
    IWorkspaceDotNetInspector dotNetInspector,
    IGoalService goalService,
    IGoalModelService goalModelService,
    IGoalWorkflowService workflowService,
    IGoalAcceptanceService acceptanceService,
    IInboundGoalOperationCoordinator goalOperations,
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
        Read("harness_read_range"), Read("harness_git"), Read("harness_git_history"),
        Read("harness_git_commit"), Read("harness_git_blame"), Read("harness_project_graph"),
        Read("harness_goals"), Read("harness_evidence"),
        Read("harness_workflow_evidence"), Read("harness_ui", sensitive: true),
        Read("harness_goal_models"), Read("harness_commit_preview"),
        Read("harness_audit", sensitive: true), Read("harness_code_problems"),
        Read("harness_code_symbol"), Read("harness_code_definition"),
        Read("harness_code_references"), Read("harness_code_implementations"),
        Read("harness_code_inspection"), Read("harness_code_actions"),
        Read("harness_inspect_capture", sensitive: true),
        Read("harness_evaluation_snapshot", sensitive: true),
        Action("harness_create_goal", idempotent: false),
        Action("harness_configure_goal"),
        Action("harness_extend_goal_budget"),
        Action("harness_select_goal_model"),
        Action("harness_start_planning", execution: true),
        Action("harness_resume_goal", execution: true),
        Action("harness_retry_goal", execution: true),
        Action("harness_cancel_goal_operation", idempotent: true),
        Action("harness_abort_goal", destructive: true, idempotent: true),
        Action("harness_decide_plan", idempotent: true),
        Action("harness_request_commit"),
        Action("harness_decide_commit", idempotent: true),
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
            toolDiscovery = "Use MCP tools/list for the authoritative exposed tool list.",
            exposedTools = context.ExposedTools?.Select(tool => tool.Value).ToArray() ?? [],
            toolPolicies = ToolPolicies,
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

    public async ValueTask<InboundMcpApplicationResult> GetGitHistoryAsync(
        InboundMcpCallContext context,
        InboundMcpGitHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        DeveloperGitHistoryPageView result = await developerGitService.InspectHistoryAsync(
            new(
                WorkspaceRequest(workspace, request.GoalId),
                string.IsNullOrWhiteSpace(request.RelativePath)
                    ? null
                    : new Harness.BusinessLogic.Inspection.DeveloperGitPath(request.RelativePath),
                string.IsNullOrWhiteSpace(request.Cursor)
                    ? null
                    : new Harness.BusinessLogic.Inspection.DeveloperGitHistoryCursor(request.Cursor),
                request.MaximumResults),
            cancellationToken);
        return GitSuccess(context, workspace, result.Context, result);
    }

    public async ValueTask<InboundMcpApplicationResult> GetGitCommitAsync(
        InboundMcpCallContext context,
        InboundMcpGitCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        Harness.BusinessLogic.Inspection.DeveloperGitCommitDetailResult result =
            await developerGitService.InspectCommitAsync(
                WorkspaceRequest(workspace, request.GoalId),
                new Harness.BusinessLogic.Inspection.DeveloperGitCommitSha(request.CommitSha),
                cancellationToken);
        return GitSuccess(context, workspace, result.Context, result);
    }

    public async ValueTask<InboundMcpApplicationResult> GetGitBlameAsync(
        InboundMcpCallContext context,
        InboundMcpGitBlameRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        DeveloperGitBlamePageView result = await developerGitService.InspectBlameAsync(
            new(
                WorkspaceRequest(workspace, request.GoalId),
                new Harness.BusinessLogic.Inspection.DeveloperGitPath(request.RelativePath),
                request.StartLine,
                request.MaximumLines),
            cancellationToken);
        return GitSuccess(context, workspace, result.Context, result);
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
        InboundMcpCallContext context,
        InboundMcpGoalListRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        if (!TryPage(request.MaximumResults, request.Continuation, out int offset,
                out InboundMcpApplicationResult? pageFailure))
            return pageFailure!;
        IReadOnlyList<GoalView> allGoals = await goalService.ListAsync(
            workspace.Id, cancellationToken);
        GoalView[] matchingGoals = allGoals
            .Where(goal => request.GoalId is null ||
                goal.Id.Value.Equals(request.GoalId, StringComparison.Ordinal))
            .OrderByDescending(goal => goal.UpdatedAt)
            .ThenBy(goal => goal.Id.Value, StringComparer.Ordinal)
            .ToArray();
        GoalView[] goals = matchingGoals
            .Skip(offset)
            .Take(request.MaximumResults)
            .ToArray();
        List<object> details = [];
        foreach (GoalView goal in goals)
        {
            GoalWorkflowSnapshot? workflow = await workflowService.GetLatestAsync(
                goal.Id, cancellationToken);
            InboundGoalOperationView? operation = goalOperations.Get(goal.Id);
            details.Add(new
            {
                goal,
                plan = await goalService.GetCurrentPlanAsync(goal.Id, cancellationToken),
                workflow = workflow is null ? null : new
                {
                    workflow.Id,
                    workflow.GoalId,
                    workflow.State,
                    workflow.ReviewCycle,
                    workflow.Tasks,
                    workflow.Activities,
                    workflow.CanResume,
                    workflow.RequiresUserDirection,
                    workflow.RetryRole,
                },
                cost = await remoteCostService.GetAsync(goal.Id, cancellationToken),
                inboundOperation = operation is null ? null : new
                {
                    operation.Id,
                    operation.GoalId,
                    operation.Kind,
                    operation.State,
                    operation.StartedAt,
                    operation.CompletedAt,
                    operation.Error,
                },
            });
        }
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            workspace.Id,
            goals = details,
            totalMatches = matchingGoals.Length,
            continuation = NextContinuation(offset, goals.Length, matchingGoals.Length),
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> ListEvidenceAsync(
        InboundMcpCallContext context, InboundMcpEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        if (!TryPage(request.MaximumResults, request.Continuation, out int offset,
                out InboundMcpApplicationResult? pageFailure))
            return pageFailure!;
        GoalView? goal = await goalService.GetAsync(new(request.GoalId), cancellationToken);
        if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            return Failure("goal_unavailable", "The goal is not part of the active workspace.");
        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(request.GoalId, cancellationToken);
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            goal.Id,
            evidence = evidence with
            {
                Items = evidence.Items.Skip(offset).Take(request.MaximumResults).ToArray(),
            },
            totalMatches = evidence.Items.Count,
            continuation = NextContinuation(
                offset,
                Math.Min(request.MaximumResults, Math.Max(0, evidence.Items.Count - offset)),
                evidence.Items.Count),
            freshness = context.RequestedAt
        });
    }

    public async ValueTask<InboundMcpApplicationResult> ListWorkflowEvidenceAsync(
        InboundMcpCallContext context, InboundMcpWorkflowEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return WorkspaceFailure(context);
        if (!TryPage(request.MaximumResults, request.Continuation, out int offset,
                out InboundMcpApplicationResult? pageFailure))
            return pageFailure!;
        GoalView? goal = await goalService.GetAsync(new(request.GoalId), cancellationToken);
        if (goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal))
            return Failure("goal_unavailable", "The goal is not part of the active workspace.");
        GoalWorkflowSnapshot? workflow = await workflowService.GetLatestAsync(
            goal.Id, cancellationToken);
        IReadOnlyList<WorkflowEvidenceView> evidence = workflow?.Evidence ?? [];
        WorkflowEvidenceView[] page = evidence
            .Skip(offset)
            .Take(request.MaximumResults)
            .ToArray();
        return Success(new
        {
            instanceId = context.InstanceId.Value,
            sourceContextId = SourceId(workspace),
            goal.Id,
            workflowId = workflow?.Id,
            evidence = page,
            totalMatches = evidence.Count,
            continuation = NextContinuation(offset, page.Length, evidence.Count),
            freshness = context.RequestedAt
        });
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

    public ValueTask<InboundMcpApplicationResult> InspectCodeAsync(
        InboundMcpCallContext context, InboundMcpCodeInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Kind))
            return ValueTask.FromResult(Failure(
                "invalid_code_inspection_kind", "A closed code inspection kind is required."));
        return CodePositionAsync(context,
            new(request.GoalId, request.RelativePath, request.Line, request.Character),
            (goal, path, position, token) => codeIntelligenceService.InspectAsync(
                goal, GoalWorkspaceScope.Original, path, position,
                request.Kind switch
                {
                    InboundMcpCodeInspectionKind.SyntaxTree =>
                        WorkbenchCodeInspectionKind.SyntaxTree,
                    InboundMcpCodeInspectionKind.Symbol => WorkbenchCodeInspectionKind.Symbol,
                    InboundMcpCodeInspectionKind.GeneratedSource =>
                        WorkbenchCodeInspectionKind.GeneratedSource,
                    InboundMcpCodeInspectionKind.IntermediateLanguage =>
                        WorkbenchCodeInspectionKind.IntermediateLanguage,
                    _ => throw new ArgumentOutOfRangeException(nameof(request.Kind)),
                }, token), cancellationToken);
    }

    public ValueTask<InboundMcpApplicationResult> FindCodeActionsAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default) => CodePositionAsync(context, request,
            (goal, path, position, token) => codeIntelligenceService.FindCodeActionsAsync(
                goal, GoalWorkspaceScope.Original, path, position,
                cancellationToken: token), cancellationToken);

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

    private async ValueTask<InboundMcpApplicationResult> StartGoalOperationAsync(
        InboundMcpCallContext context,
        string goalId,
        string kind,
        Func<GoalId, Func<CancellationToken, IAsyncEnumerable<GoalWorkflowSnapshot>>> workflow,
        CancellationToken cancellationToken)
    {
        (WorkspaceView? workspace, GoalView? goal, InboundMcpApplicationResult? failure) =
            await GoalAsync(context, goalId, cancellationToken);
        if (failure is not null) return failure;
        InboundGoalOperationResult result = goalOperations.Start(
            goal!.Id, kind, workflow(goal.Id));
        return result.Error is null
            ? Success(new
            {
                instanceId = context.InstanceId.Value,
                sourceContextId = SourceId(workspace!),
                result,
                freshness = context.RequestedAt,
            })
            : Failure(result.ErrorCode ?? "goal_operation_failed", result.Error);
    }

    private async ValueTask<(
        WorkspaceView? Workspace,
        GoalView? Goal,
        InboundMcpApplicationResult? Failure)> GoalAsync(
        InboundMcpCallContext context,
        string goalId,
        CancellationToken cancellationToken)
    {
        WorkspaceView? workspace = await TrustedWorkspaceAsync(context, cancellationToken);
        if (workspace is null) return (null, null, WorkspaceFailure(context));
        GoalView? goal = await goalService.GetAsync(new(goalId), cancellationToken);
        return goal is null || !goal.WorkspaceId.Equals(workspace.Id, StringComparison.Ordinal)
            ? (workspace, null,
                Failure("goal_unavailable", "The goal is not part of the active workspace."))
            : (workspace, goal, null);
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

    private static WorkbenchWorkspaceRequest WorkspaceRequest(
        WorkspaceView workspace,
        string? goalId) => new(
        new(workspace.Id),
        string.IsNullOrWhiteSpace(goalId) ? null : new GoalId(goalId));

    private static InboundMcpApplicationResult GitSuccess(
        InboundMcpCallContext callContext,
        WorkspaceView workspace,
        WorkbenchWorkspaceContext sourceContext,
        object result) => Success(new
        {
            instanceId = callContext.InstanceId.Value,
            sourceContextId = $"{workspace.Id}:{sourceContext.Scope}:" +
                (sourceContext.GoalId?.Value ?? sourceContext.Branch?.Value),
            sourceContext,
            result,
            freshness = callContext.RequestedAt,
        });

    private static bool TryPage(
        int maximumResults,
        string? continuation,
        out int offset,
        out InboundMcpApplicationResult? failure)
    {
        offset = 0;
        failure = null;
        if (maximumResults is < 1 or > 100)
        {
            failure = Failure("invalid_result_limit",
                "Maximum results must be between 1 and 100.");
            return false;
        }
        if (continuation is not null &&
            (!int.TryParse(continuation, NumberStyles.None, CultureInfo.InvariantCulture,
                out offset) || offset < 0))
        {
            failure = Failure("invalid_continuation",
                "The continuation token is invalid for this bounded result set.");
            return false;
        }
        return true;
    }

    private static string? NextContinuation(int offset, int returned, int total) =>
        offset + returned < total
            ? (offset + returned).ToString(CultureInfo.InvariantCulture)
            : null;

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
