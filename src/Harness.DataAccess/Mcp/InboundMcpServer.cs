using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Harness.DataAccess.Mcp;

internal sealed class InboundMcpServer(
    IInboundMcpSettingsStore settingsStore,
    ISecretStore secretStore,
    IInboundMcpApplication application,
    IInboundMcpAuditStore auditStore,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider) : IInboundMcpRuntime, IHostedService, IAsyncDisposable
{
    private const string ClientHeader = "X-Harness-Client";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, InboundMcpClientStatus> clients = new(StringComparer.Ordinal);
    private readonly HashSet<string> disconnected = new(StringComparer.Ordinal);
    private CancellationTokenSource requestRevocation = new();
    private readonly InboundMcpApplicationInstanceId instanceId = new(Guid.NewGuid().ToString("N"));
    private WebApplication? server;
    private InboundMcpServerSettings settings = XdgInboundMcpSettingsStore.Default;
    private string? token;
    private InboundMcpServerStatus current = new(
        new(Guid.Empty.ToString("N")), false, XdgInboundMcpSettingsStore.Default.Endpoint,
        InboundMcpMode.Normal, false, [], null, null);
    private int disposed;

    public InboundMcpServerStatus Current => current;

    public Task StartAsync(CancellationToken cancellationToken) => ApplyAsync(cancellationToken).AsTask();

    public async Task StopAsync(CancellationToken cancellationToken) =>
        await StopServerAsync(cancellationToken);

    public async ValueTask ApplyAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await StopServerCoreAsync(cancellationToken);
            settings = await settingsStore.GetAsync(cancellationToken);
            XdgInboundMcpSettingsStore.Validate(settings);
            if (!settings.IsEnabled)
            {
                Publish(false, false, null, null);
                return;
            }

            token = await secretStore.GetAsync(new(settings.TokenReference.Value), cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                token = CreateToken();
                await secretStore.SetAsync(new(settings.TokenReference.Value), token, cancellationToken);
            }

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(InboundMcpServer).Assembly.FullName,
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ForwardingLoggerProvider(loggerFactory));
            builder.WebHost.ConfigureKestrel(kestrel =>
                kestrel.ListenLocalhost(settings.Endpoint.Port));
            builder.Services.AddSingleton(application);
            builder.Services.AddSingleton(this);
            HttpContextAccessor contextAccessor = new();
            builder.Services.AddSingleton<IHttpContextAccessor>(contextAccessor);
            InboundMcpTools toolTarget = new(application, this, contextAccessor);
            McpServerTool[] allowedTools = typeof(InboundMcpTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => (Method: method,
                    Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
                .Where(item => item.Attribute?.Name is { } name && ToolAllowed(new(name)) &&
                    (application.ToolPolicies.Count == 0 || application.ToolPolicies.Any(policy =>
                        policy.Id.Value.Equals(name, StringComparison.Ordinal))))
                .Select(item => McpServerTool.Create(item.Method, toolTarget, new()))
                .ToArray();
            builder.Services.AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation { Name = "Harness.NET", Version = "1.0.0" };
                    options.ServerInstructions = "Use only discovered typed Harness.NET tools. " +
                        "This server grants no shell, SQL, arbitrary command, generic click/type, " +
                        "desktop-control, credential-read, or natural-language authority. " +
                        "Preserve returned instance, workspace, source, goal, plan, run, operation, " +
                        "approval, baseline, and continuation identities. Goal planning, retry, and " +
                        "resume return background operation identities; poll harness_goals rather " +
                        "than replaying them.";
                })
                .WithHttpTransport(options =>
                {
                    options.Stateless = true;
                })
                .WithTools(allowedTools);

            WebApplication app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (!Authenticate(context, out InboundMcpClientId clientId))
                {
                    await RecordAuditAsync(clientId, null, InboundMcpAuditOutcome.Denied,
                        "authentication_denied", context.RequestAborted);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                RecordClient(clientId);
                await RecordAuditAsync(clientId, null, InboundMcpAuditOutcome.Allowed,
                    null, context.RequestAborted);
                using CancellationTokenSource timeout = CancellationTokenSource
                    .CreateLinkedTokenSource(context.RequestAborted, RequestRevocationToken());
                timeout.CancelAfter(settings.RequestTimeout.Value);
                context.RequestAborted = timeout.Token;
                await next(context);
            });
            app.MapMcp(settings.Endpoint.AbsolutePath);
            try
            {
                await app.StartAsync(cancellationToken);
                server = app;
                Publish(true, true, null, null);
            }
            catch (Exception exception)
            {
                await app.DisposeAsync();
                Publish(false, true, "inbound_mcp_start_failed", exception.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<InboundMcpBearerToken> RotateTokenAsync(
        CancellationToken cancellationToken = default)
    {
        string replacement = CreateToken();
        await secretStore.SetAsync(new(settings.TokenReference.Value), replacement, cancellationToken);
        token = replacement;
        RevokeInFlightRequests();
        lock (clients)
        {
            disconnected.Clear();
            clients.Clear();
        }
        RevokeInFlightRequests();
        Publish(server is not null, true, current.ErrorCode, current.Error);
        return new(replacement);
    }

    public ValueTask DisconnectAsync(
        InboundMcpClientId clientId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (clients)
        {
            disconnected.Add(clientId.Value);
            clients.Remove(clientId.Value);
        }
        Publish(server is not null, token is not null, current.ErrorCode, current.Error);
        return ValueTask.CompletedTask;
    }

    internal bool ToolAllowed(InboundMcpToolId tool) =>
        settings.AllowedTools.Any(item => item.Value.Equals(tool.Value, StringComparison.Ordinal)) &&
        !settings.ApprovalRequiredTools.Any(item => item.Value.Equals(tool.Value, StringComparison.Ordinal));

    internal InboundMcpCallContext Context(HttpContext httpContext) => new(
        instanceId,
        new(httpContext.Request.Headers[ClientHeader].ToString()),
        settings.Mode,
        timeProvider.GetUtcNow());

    internal bool MatchesInstance(string expectedInstanceId) =>
        instanceId.Value.Equals(expectedInstanceId, StringComparison.Ordinal);

    internal ValueTask RecordAuditAsync(
        InboundMcpClientId clientId, InboundMcpToolId? tool, InboundMcpAuditOutcome outcome,
        string? errorCode, CancellationToken cancellationToken = default,
        DateTimeOffset? startedAt = null)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return auditStore.AppendAsync(new(Guid.NewGuid().ToString("N"), instanceId, clientId,
            tool, settings.Mode, outcome, startedAt ?? now, now, errorCode), settings.AuditRetention.Value,
            cancellationToken);
    }

    internal ValueTask<IReadOnlyList<InboundMcpAuditRecord>> ListAuditAsync(
        int maximumResults, CancellationToken cancellationToken) =>
        auditStore.ListAsync(maximumResults, cancellationToken);

    private bool Authenticate(HttpContext context, out InboundMcpClientId clientId)
    {
        string client = context.Request.Headers[ClientHeader].ToString().Trim();
        clientId = new(client);
        string authorization = context.Request.Headers.Authorization.ToString();
        string supplied = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim() : string.Empty;
        bool tokenMatches = token is not null && supplied.Length == token.Length &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token));
        lock (clients)
        {
            return tokenMatches && client.Length is > 0 and <= 128 &&
                !disconnected.Contains(client) &&
                (settings.AllowedClients.Count == 0 ||
                 settings.AllowedClients.Any(item => item.Value.Equals(client, StringComparison.Ordinal)));
        }
    }

    private void RecordClient(InboundMcpClientId clientId)
    {
        lock (clients)
        {
            clients.TryGetValue(clientId.Value, out InboundMcpClientStatus? existing);
            clients[clientId.Value] = new(clientId, timeProvider.GetUtcNow(),
                (existing?.RequestCount ?? 0) + 1);
        }
        Publish(true, true, current.ErrorCode, current.Error);
    }

    private CancellationToken RequestRevocationToken()
    {
        lock (clients) return requestRevocation.Token;
    }

    private void RevokeInFlightRequests()
    {
        CancellationTokenSource previous;
        lock (clients)
        {
            previous = requestRevocation;
            requestRevocation = new();
        }
        previous.Cancel();
        previous.Dispose();
    }

    private void Publish(bool running, bool authenticated, string? errorCode, string? error)
    {
        InboundMcpClientStatus[] snapshot;
        lock (clients) snapshot = clients.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        current = new(instanceId, running, settings.Endpoint, settings.Mode, authenticated,
            snapshot, errorCode, error);
    }

    private async ValueTask StopServerAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await StopServerCoreAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    private async ValueTask StopServerCoreAsync(CancellationToken cancellationToken)
    {
        WebApplication? active = server;
        server = null;
        token = null;
        RevokeInFlightRequests();
        if (active is not null)
        {
            await active.StopAsync(cancellationToken);
            await active.DisposeAsync();
        }
        lock (clients) clients.Clear();
        Publish(false, false, null, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await StopServerAsync(CancellationToken.None);
        lock (clients) requestRevocation.Dispose();
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private sealed class ForwardingLoggerProvider(ILoggerFactory factory) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => factory.CreateLogger(categoryName);
        public void Dispose() { }
    }
}

[McpServerToolType]
internal sealed class InboundMcpTools(
    IInboundMcpApplication application,
    InboundMcpServer runtime,
    IHttpContextAccessor contextAccessor)
{
    [McpServerTool(Name = "harness_application", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Inspect this Harness.NET process, inbound MCP mode, and exact application instance identity.")]
    public ValueTask<string> ApplicationAsync(CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_application"), context =>
            application.GetApplicationAsync(context, cancellationToken));

    [McpServerTool(Name = "harness_evaluation_snapshot", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Inspect the disposable fixture HEAD, changes, and paths of this isolated Harness.NET evaluation process.")]
    public ValueTask<string> EvaluationSnapshotAsync(CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_evaluation_snapshot"), context =>
            application.GetEvaluationSnapshotAsync(context, cancellationToken));

    [McpServerTool(Name = "harness_evaluation_reset", ReadOnly = false,
        Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Reset only this process's disposable evaluation fixture to its deterministic baseline. Normal repositories cannot be addressed.")]
    public ValueTask<string> EvaluationResetAsync(
        string expectedInstanceId, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_evaluation_reset"), context =>
            application.ResetEvaluationAsync(context, cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_workspace", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Inspect the active Harness.NET workspace and source identity without changing selection or trust.")]
    public ValueTask<string> WorkspaceAsync(CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_workspace"), context =>
            application.GetWorkspaceAsync(context, cancellationToken));

    [McpServerTool(Name = "harness_tree", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("List a bounded page of Git-tracked paths in the active trusted Harness.NET workspace.")]
    public ValueTask<string> TreeAsync(
        string relativeRoot, string? glob, int maximumDepth, int maximumResults,
        string? continuation, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_tree"), context => application.ListTreeAsync(context,
            new(relativeRoot, glob, maximumDepth, maximumResults, continuation), cancellationToken));

    [McpServerTool(Name = "harness_read_range", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Read a bounded one-based line range from one Git-tracked UTF-8 file in the active workspace.")]
    public ValueTask<string> ReadRangeAsync(
        string relativePath, int startLine, int lineCount, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_read_range"), context => application.ReadRangeAsync(context,
            new(relativePath, startLine, lineCount), cancellationToken));

    [McpServerTool(Name = "harness_git", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Inspect active-workspace branch, HEAD, status, and bounded diff without Git mutation.")]
    public ValueTask<string> GitAsync(CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_git"), context => application.GetGitAsync(context, cancellationToken));

    [McpServerTool(Name = "harness_project_graph", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Inspect active-workspace .NET projects, targets, package references, and direct project edges without restore.")]
    public ValueTask<string> ProjectGraphAsync(CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_project_graph"), context =>
            application.GetProjectGraphAsync(context, cancellationToken));

    [McpServerTool(Name = "harness_goals", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("List a bounded page of goals for the active workspace, or inspect one exact goal. Workflow prompts and evidence are available separately and are not duplicated here.")]
    public ValueTask<string> GoalsAsync(
        string? goalId, int maximumResults, string? continuation,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_goals"), context => application.ListGoalsAsync(context,
            new(goalId, maximumResults, continuation), cancellationToken));

    [McpServerTool(Name = "harness_evidence", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("List a bounded page of durable tool and verification evidence for one exact Harness.NET goal.")]
    public ValueTask<string> EvidenceAsync(
        string goalId, int maximumResults, string? continuation,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_evidence"), context => application.ListEvidenceAsync(
            context, new(goalId, maximumResults, continuation), cancellationToken));

    [McpServerTool(Name = "harness_workflow_evidence", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List a bounded page of workflow prompts, recovery notices, and model or tool evidence for one exact Harness.NET goal.")]
    public ValueTask<string> WorkflowEvidenceAsync(
        string goalId, int maximumResults, string? continuation,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_workflow_evidence"), context =>
            application.ListWorkflowEvidenceAsync(context,
                new(goalId, maximumResults, continuation), cancellationToken));

    [McpServerTool(Name = "harness_create_goal", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = false)]
    [Description("Create one draft goal in the exact active trusted workspace. This grants no model, spending, execution, worktree, or mutation authority.")]
    public ValueTask<string> CreateGoalAsync(
        string expectedInstanceId, string workspaceId, string title, string objective,
        int reviewCycleLimit, long? remoteBudgetMicrousd,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_create_goal"), context => application.CreateGoalAsync(
            context, new(workspaceId, title, objective, reviewCycleLimit,
                remoteBudgetMicrousd), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_configure_goal", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Update one exact draft goal's review limit and remote monetary cap using its returned updatedAt baseline. Null remoteBudgetMicrousd means local-only.")]
    public ValueTask<string> ConfigureGoalAsync(
        string expectedInstanceId, string goalId, int reviewCycleLimit,
        long? remoteBudgetMicrousd, DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_configure_goal"), context =>
            application.UpdateGoalSettingsAsync(context,
                new(goalId, reviewCycleLimit, remoteBudgetMicrousd, expectedUpdatedAt),
                cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_extend_goal_budget", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Increase one goal's remote monetary cap from an exact current cap with an explicit reason. This cannot reduce or create other authority.")]
    public ValueTask<string> ExtendGoalBudgetAsync(
        string expectedInstanceId, string goalId, long? expectedBudgetMicrousd,
        long newBudgetMicrousd, string reason, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_extend_goal_budget"), context =>
            application.ExtendGoalBudgetAsync(context,
                new(goalId, expectedBudgetMicrousd, newBudgetMicrousd, reason),
                cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_goal_models", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = true)]
    [Description("Discover one bounded, filterable page of models from configured providers for an exact draft goal and return its current per-role selections. Catalog discovery performs no inference.")]
    public ValueTask<string> GoalModelsAsync(
        string goalId, string? provider, string? role, string? search,
        int maximumResults, string? continuation, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_goal_models"), context =>
            application.DiscoverGoalModelsAsync(context,
                new(goalId, provider, role, search, maximumResults, continuation),
                cancellationToken));

    [McpServerTool(Name = "harness_select_goal_model", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Select one discovered fully role-compatible provider/model route for Lead, Implementer, or Reviewer on one exact draft goal.")]
    public ValueTask<string> SelectGoalModelAsync(
        string expectedInstanceId, string goalId, string role, string provider,
        string model, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_select_goal_model"), context =>
            application.SelectGoalModelAsync(context,
                new(goalId, role, provider, model), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_start_planning", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Start one background Lead planning operation for an exact draft goal using its selected route and spending policy. Returns immediately; poll harness_goals with the returned operation identity.")]
    public ValueTask<string> StartPlanningAsync(
        string expectedInstanceId, string goalId, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_start_planning"), context =>
            application.StartGoalPlanningAsync(context, new(goalId), cancellationToken),
            expectedInstanceId);

    [McpServerTool(Name = "harness_resume_goal", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Start one background resume operation at the exact durable boundary of an approved or paused goal. Existing worktree, spend, evidence, and transition checks apply.")]
    public ValueTask<string> ResumeGoalAsync(
        string expectedInstanceId, string goalId, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_resume_goal"), context =>
            application.ResumeGoalAsync(context, new(goalId), cancellationToken),
            expectedInstanceId);

    [McpServerTool(Name = "harness_retry_goal", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Start one explicit background retry for the failed Lead, Implementer, or Reviewer call, with optional bounded guidance. No prior model call is replayed implicitly.")]
    public ValueTask<string> RetryGoalAsync(
        string expectedInstanceId, string goalId, string role, string? guidance,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_retry_goal"), context =>
            application.RetryGoalAsync(context, new(goalId, role, guidance),
                cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_cancel_goal_operation", ReadOnly = false,
        Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cancel one exact active inbound goal operation. The workflow preserves its last durable boundary and records uncertainty where required.")]
    public ValueTask<string> CancelGoalOperationAsync(
        string expectedInstanceId, string goalId, string operationId,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_cancel_goal_operation"), context =>
            application.CancelGoalOperationAsync(context, new(goalId, operationId),
                cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_abort_goal", ReadOnly = false, Destructive = true,
        Idempotent = true, OpenWorld = false)]
    [Description("Abort one exact inactive goal workflow with an explicit reason. Cancel any active operation first. This starts no replacement goal and deletes no repository content.")]
    public ValueTask<string> AbortGoalAsync(
        string expectedInstanceId, string goalId, string reason,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_abort_goal"), context => application.AbortGoalAsync(
            context, new(goalId, reason), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_decide_plan", ReadOnly = false, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Approve or deny one exact current Harness.NET plan. Existing plan state, worktree, trust, and baseline checks apply.")]
    public ValueTask<string> DecidePlanAsync(
        string expectedInstanceId, string goalId, string planId, string decision, string? reason,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_decide_plan"), context => application.DecidePlanAsync(
            context, new(goalId, planId, decision, reason), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_build", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = false)]
    [Description("Run the existing bounded no-restore Build command for an approved goal worktree. Existing trust and execution checks apply.")]
    public ValueTask<string> BuildAsync(
        string expectedInstanceId, string goalId, string correlationId,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_build"), context => application.BuildAsync(
            context, new(goalId, correlationId), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_test", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = false)]
    [Description("Run the existing bounded no-restore Test command for an approved goal worktree. Existing trust and execution checks apply.")]
    public ValueTask<string> TestAsync(
        string expectedInstanceId, string goalId, string correlationId,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_test"), context => application.TestAsync(
            context, new(goalId, correlationId), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_commit_preview", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Preview the complete exact diff, branch HEAD, and fingerprint of an accepted goal worktree without committing or integrating it.")]
    public ValueTask<string> CommitPreviewAsync(
        string goalId, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_commit_preview"), context =>
            application.PreviewCommitAsync(context, new(goalId), cancellationToken));

    [McpServerTool(Name = "harness_request_commit", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create one commit approval request for an exact accepted run, branch HEAD, and complete diff fingerprint. This does not commit.")]
    public ValueTask<string> RequestCommitAsync(
        string expectedInstanceId, string goalId, string runId, string expectedHead,
        string expectedDiffHash, string message, string authorName, string authorEmail,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_request_commit"), context =>
            application.RequestCommitApprovalAsync(context, new(
                goalId, runId, expectedHead, expectedDiffHash, message, authorName,
                authorEmail), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_decide_commit", ReadOnly = false,
        Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Approve or deny one exact commit approval. Approval revalidates HEAD and the complete diff, commits only the isolated goal branch, and never merges it.")]
    public ValueTask<string> DecideCommitAsync(
        string expectedInstanceId, string goalId, string runId, string approvalId,
        string decision, string? reason, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_decide_commit"), context => application.DecideCommitAsync(
            context, new(goalId, runId, approvalId, decision, reason), cancellationToken),
            expectedInstanceId);

    [McpServerTool(Name = "harness_ui", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Inspect the running Harness.NET window and its closed accessibility action IDs. This does not capture another application or activate input.")]
    public ValueTask<string> UiAsync(CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_ui"), context => application.GetUiAsync(context, cancellationToken));

    [McpServerTool(Name = "harness_ui_activate", ReadOnly = false, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Activate one advertised Harness.NET accessibility action ID in isolated evaluation mode. No coordinates, text entry, global input, or other process can be addressed.")]
    public ValueTask<string> ActivateUiAsync(
        string expectedInstanceId, string actionId, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_ui_activate"), context => application.ActivateUiAsync(
            context, new(actionId), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_open_document", ReadOnly = false, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Open or focus one bounded tracked relative document in Harness.NET. This changes only Harness UI selection and grants no file authority.")]
    public ValueTask<string> OpenDocumentAsync(
        string expectedInstanceId, string relativePath, string? goalId,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_open_document"), context => application.OpenDocumentAsync(
            context, new(relativePath, goalId), cancellationToken), expectedInstanceId);

    [McpServerTool(Name = "harness_request_capture", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = true)]
    [Description("Ask the user through XDG Desktop Portal to approve one goal-bound screenshot. No silent capture or input control is performed.")]
    public ValueTask<string> RequestCaptureAsync(
        string expectedInstanceId, string goalId, string correlationId,
        string relatedAction, string target,
        CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_request_capture"), context => application.RequestCaptureAsync(
            context, new(goalId, correlationId, relatedAction, target), cancellationToken),
            expectedInstanceId);

    [McpServerTool(Name = "harness_inspect_capture", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Inspect one exact bounded Harness.NET visual-capture evidence item by goal and capture ID under existing disclosure policy.")]
    public ValueTask<string> InspectCaptureAsync(
        string goalId, string captureId, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_inspect_capture"), context => application.InspectCaptureAsync(
            context, new(goalId, captureId), cancellationToken));

    [McpServerTool(Name = "harness_audit", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("List bounded recent inbound MCP authentication and tool-call audit records. Arguments and results are not repeated in this log.")]
    public async ValueTask<string> AuditAsync(int maximumResults, CancellationToken cancellationToken)
    {
        HttpContext? http = contextAccessor.HttpContext;
        if (http is null || !runtime.ToolAllowed(new("harness_audit")))
            throw new McpException("inbound_mcp_tool_denied: The audit tool is unavailable.");
        InboundMcpClientId client = new(http.Request.Headers["X-Harness-Client"].ToString());
        DateTimeOffset started = runtime.Context(http).RequestedAt;
        string result = JsonSerializer.Serialize(
            await runtime.ListAuditAsync(maximumResults, cancellationToken));
        await runtime.RecordAuditAsync(client, new("harness_audit"),
            InboundMcpAuditOutcome.Succeeded, null, cancellationToken, started);
        return result;
    }

    [McpServerTool(Name = "harness_code_problems", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Ask Roslyn for exact-baseline diagnostics in one tracked C# file for one goal's original trusted source context.")]
    public ValueTask<string> CodeProblemsAsync(
        string goalId, string relativePath, CancellationToken cancellationToken) =>
        InvokeAsync(new("harness_code_problems"), context => application.InspectCodeProblemsAsync(
            context, new(goalId, relativePath), cancellationToken));

    [McpServerTool(Name = "harness_code_symbol", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Ask Roslyn for the exact symbol signature, documentation, and destination at a zero-based source position.")]
    public ValueTask<string> CodeSymbolAsync(
        string goalId, string relativePath, int line, int character,
        CancellationToken cancellationToken) => CodePositionAsync(new("harness_code_symbol"),
            application.GetCodeSymbolAsync, goalId, relativePath, line, character, cancellationToken);

    [McpServerTool(Name = "harness_code_definition", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Resolve the Roslyn definition of the symbol at a zero-based source position.")]
    public ValueTask<string> CodeDefinitionAsync(
        string goalId, string relativePath, int line, int character,
        CancellationToken cancellationToken) => CodePositionAsync(new("harness_code_definition"),
            application.FindCodeDefinitionAsync, goalId, relativePath, line, character, cancellationToken);

    [McpServerTool(Name = "harness_code_references", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Find bounded Roslyn references of the symbol at a zero-based source position.")]
    public ValueTask<string> CodeReferencesAsync(
        string goalId, string relativePath, int line, int character,
        CancellationToken cancellationToken) => CodePositionAsync(new("harness_code_references"),
            application.FindCodeReferencesAsync, goalId, relativePath, line, character, cancellationToken);

    [McpServerTool(Name = "harness_code_implementations", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Find bounded Roslyn implementations and overrides at a zero-based source position.")]
    public ValueTask<string> CodeImplementationsAsync(
        string goalId, string relativePath, int line, int character,
        CancellationToken cancellationToken) => CodePositionAsync(new("harness_code_implementations"),
            application.FindCodeImplementationsAsync, goalId, relativePath, line, character, cancellationToken);

    [McpServerTool(Name = "harness_code_actions", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false)]
    [Description("Find closed Roslyn quick fixes and local refactorings at a zero-based source position without changing the repository.")]
    public ValueTask<string> CodeActionsAsync(
        string goalId, string relativePath, int line, int character,
        CancellationToken cancellationToken) => CodePositionAsync(new("harness_code_actions"),
            application.FindCodeActionsAsync, goalId, relativePath, line, character,
            cancellationToken);

    private ValueTask<string> CodePositionAsync(
        InboundMcpToolId tool,
        Func<InboundMcpCallContext, InboundMcpCodePositionRequest, CancellationToken,
            ValueTask<InboundMcpApplicationResult>> operation,
        string goalId, string relativePath, int line, int character,
        CancellationToken cancellationToken) => InvokeAsync(tool, context => operation(
            context, new(goalId, relativePath, line, character), cancellationToken));

    private async ValueTask<string> InvokeAsync(
        InboundMcpToolId tool,
        Func<InboundMcpCallContext, ValueTask<InboundMcpApplicationResult>> call,
        string? expectedInstanceId = null)
    {
        HttpContext? http = contextAccessor.HttpContext;
        if (http is null)
            throw new McpException("inbound_mcp_context_missing: The authenticated HTTP context is unavailable.");
        InboundMcpClientId client = new(http.Request.Headers["X-Harness-Client"].ToString());
        if (expectedInstanceId is not null && !runtime.MatchesInstance(expectedInstanceId))
        {
            await runtime.RecordAuditAsync(client, tool, InboundMcpAuditOutcome.Denied,
                "stale_application_instance", http.RequestAborted);
            throw new McpException(
                "stale_application_instance: Refresh harness_application and retry against the current process.");
        }
        if (!runtime.ToolAllowed(tool))
        {
            await runtime.RecordAuditAsync(client, tool, InboundMcpAuditOutcome.Denied,
                "inbound_mcp_tool_denied", http.RequestAborted);
            throw new McpException("inbound_mcp_tool_denied: This tool is disabled or requires approval.");
        }
        DateTimeOffset started = runtime.Context(http).RequestedAt;
        try
        {
            InboundMcpApplicationResult result = await call(runtime.Context(http));
            await runtime.RecordAuditAsync(client, tool,
                result.IsError ? InboundMcpAuditOutcome.Failed : InboundMcpAuditOutcome.Succeeded,
                result.ErrorCode, http.RequestAborted, started);
            if (result.IsError) throw new McpException($"{result.ErrorCode}: {result.Error}");
            return result.Json;
        }
        catch (OperationCanceledException)
        {
            await runtime.RecordAuditAsync(client, tool, InboundMcpAuditOutcome.Cancelled,
                "cancelled", CancellationToken.None, started);
            throw;
        }
        catch (Exception exception) when (exception is not McpException)
        {
            await runtime.RecordAuditAsync(client, tool, InboundMcpAuditOutcome.Failed,
                "unhandled_tool_failure", CancellationToken.None, started);
            throw;
        }
    }
}
