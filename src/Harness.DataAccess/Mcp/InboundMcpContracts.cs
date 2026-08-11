namespace Harness.DataAccess.Mcp;

public enum InboundMcpMode
{
    Normal,
    IsolatedEvaluation,
}

public sealed record InboundMcpApplicationInstanceId(string Value);
public sealed record InboundMcpClientId(string Value);
public sealed record InboundMcpToolId(string Value);
public sealed record InboundMcpBearerTokenReference(string Value);
public sealed record InboundMcpBearerToken(string Value);
public sealed record InboundMcpRequestTimeout(TimeSpan Value);
public sealed record InboundMcpResultLimit(int Value);
public sealed record InboundMcpAuditRetention(int Value);

public sealed record InboundMcpServerSettings(
    bool IsEnabled,
    InboundMcpMode Mode,
    Uri Endpoint,
    InboundMcpBearerTokenReference TokenReference,
    IReadOnlyList<InboundMcpClientId> AllowedClients,
    IReadOnlyList<InboundMcpToolId> AllowedTools,
    IReadOnlyList<InboundMcpToolId> ApprovalRequiredTools,
    InboundMcpRequestTimeout RequestTimeout,
    InboundMcpResultLimit ResultLimit,
    InboundMcpAuditRetention AuditRetention,
    bool RequiresRestart);

public sealed record InboundMcpClientStatus(
    InboundMcpClientId Id,
    DateTimeOffset LastSeenAt,
    int RequestCount);

public sealed record InboundMcpServerStatus(
    InboundMcpApplicationInstanceId InstanceId,
    bool IsRunning,
    Uri Endpoint,
    InboundMcpMode Mode,
    bool IsAuthenticated,
    IReadOnlyList<InboundMcpClientStatus> ActiveClients,
    string? ErrorCode,
    string? Error);

public enum InboundMcpAuditOutcome { Allowed, Denied, Succeeded, Failed, Cancelled }
public sealed record InboundMcpAuditRecord(
    string Id,
    InboundMcpApplicationInstanceId InstanceId,
    InboundMcpClientId ClientId,
    InboundMcpToolId? Tool,
    InboundMcpMode Mode,
    InboundMcpAuditOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ErrorCode);

public interface IInboundMcpAuditStore
{
    ValueTask AppendAsync(InboundMcpAuditRecord record, int retention,
        CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<InboundMcpAuditRecord>> ListAsync(
        int maximumResults, CancellationToken cancellationToken = default);
}

public sealed record InboundMcpCallContext(
    InboundMcpApplicationInstanceId InstanceId,
    InboundMcpClientId ClientId,
    InboundMcpMode Mode,
    DateTimeOffset RequestedAt);

public sealed record InboundMcpTreeRequest(
    string RelativeRoot,
    string? Glob,
    int MaximumDepth,
    int MaximumResults,
    string? Continuation);

public sealed record InboundMcpRangeRequest(string RelativePath, int StartLine, int LineCount);
public sealed record InboundMcpGoalRequest(string GoalId);
public sealed record InboundMcpPlanDecisionRequest(
    string GoalId, string PlanId, string Decision, string? Reason);
public sealed record InboundMcpExecutionRequest(string GoalId, string CorrelationId);
public sealed record InboundMcpUiActionRequest(string ActionId);
public sealed record InboundMcpOpenDocumentRequest(string RelativePath, string? GoalId);
public sealed record InboundMcpCaptureRequest(
    string GoalId, string CorrelationId, string RelatedAction, string Target);
public sealed record InboundMcpCaptureInspectionRequest(string GoalId, string CaptureId);
public sealed record InboundMcpCodeRequest(string GoalId, string RelativePath);
public sealed record InboundMcpCodePositionRequest(
    string GoalId, string RelativePath, int Line, int Character);

public sealed record InboundMcpApplicationResult(string Json, bool IsError, string? ErrorCode, string? Error);
public sealed record InboundMcpToolPolicy(
    InboundMcpToolId Id,
    bool IsReadOnly,
    bool IsMutation,
    bool IsExecution,
    bool IsSensitive,
    bool IsDestructive,
    bool IsIdempotent);

public interface IInboundMcpApplication
{
    IReadOnlyList<InboundMcpToolPolicy> ToolPolicies => [];
    ValueTask<InboundMcpApplicationResult> GetApplicationAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> GetEvaluationSnapshotAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);
    ValueTask<InboundMcpApplicationResult> ResetEvaluationAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> GetWorkspaceAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> ListTreeAsync(
        InboundMcpCallContext context, InboundMcpTreeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> ReadRangeAsync(
        InboundMcpCallContext context, InboundMcpRangeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> GetGitAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> GetProjectGraphAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> ListGoalsAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> ListEvidenceAsync(
        InboundMcpCallContext context, InboundMcpGoalRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> DecidePlanAsync(
        InboundMcpCallContext context, InboundMcpPlanDecisionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> BuildAsync(
        InboundMcpCallContext context, InboundMcpExecutionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> TestAsync(
        InboundMcpCallContext context, InboundMcpExecutionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> GetUiAsync(
        InboundMcpCallContext context, CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> ActivateUiAsync(
        InboundMcpCallContext context, InboundMcpUiActionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> OpenDocumentAsync(
        InboundMcpCallContext context, InboundMcpOpenDocumentRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> RequestCaptureAsync(
        InboundMcpCallContext context, InboundMcpCaptureRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> InspectCaptureAsync(
        InboundMcpCallContext context, InboundMcpCaptureInspectionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<InboundMcpApplicationResult> InspectCodeProblemsAsync(
        InboundMcpCallContext context, InboundMcpCodeRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<InboundMcpApplicationResult> GetCodeSymbolAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<InboundMcpApplicationResult> FindCodeDefinitionAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<InboundMcpApplicationResult> FindCodeReferencesAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<InboundMcpApplicationResult> FindCodeImplementationsAsync(
        InboundMcpCallContext context, InboundMcpCodePositionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInboundMcpSettingsStore
{
    ValueTask<InboundMcpServerSettings> GetAsync(CancellationToken cancellationToken = default);
    ValueTask<InboundMcpServerSettings> SaveAsync(
        InboundMcpServerSettings settings, CancellationToken cancellationToken = default);
}

public interface IInboundMcpRuntime
{
    InboundMcpServerStatus Current { get; }
    ValueTask ApplyAsync(CancellationToken cancellationToken = default);
    ValueTask<InboundMcpBearerToken> RotateTokenAsync(CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(InboundMcpClientId clientId, CancellationToken cancellationToken = default);
}
