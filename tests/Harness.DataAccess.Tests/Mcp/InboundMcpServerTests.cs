using System.Net;
using System.Net.Sockets;
using Harness.DataAccess.Mcp;
using Harness.DataAccess.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Harness.DataAccess.Tests.Mcp;

public sealed class InboundMcpServerTests
{
    [Fact]
    public async Task Stateless_server_requires_authentication_and_exposes_only_closed_tools()
    {
        int port = FreePort();
        Uri endpoint = new($"http://127.0.0.1:{port}/mcp");
        StaticSettings settings = new(Settings(endpoint));
        MemorySecrets secrets = new("test-token");
        RecordingApplication application = new();
        MemoryAuditStore audit = new();
        await using InboundMcpServer server = new(settings, secrets, application,
            audit, NullLoggerFactory.Instance, TimeProvider.System);
        await server.ApplyAsync();

        using HttpClient anonymous = new();
        using HttpResponseMessage denied = await anonymous.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Name = "test",
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer test-token",
                ["X-Harness-Client"] = "test-client",
            },
        }, NullLoggerFactory.Instance);
        await using McpClient client = await McpClient.CreateAsync(
            transport, loggerFactory: NullLoggerFactory.Instance);
        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Equal(16, tools.Count);
        Assert.Contains("no shell", client.ServerInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.All(tools, tool => Assert.StartsWith("harness_", tool.Name, StringComparison.Ordinal));
        Assert.Contains(tools, tool => tool.Name == "harness_code_inspection");
        Assert.All(tools.Where(tool => tool.Name is not "harness_open_document" and
                not "harness_create_goal" and not "harness_select_goal_model" and
                not "harness_abort_goal"),
            tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.False(tools.Single(tool => tool.Name == "harness_open_document")
            .ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(tool => tool.Name == "harness_abort_goal")
            .ProtocolTool.Annotations?.DestructiveHint);
        Assert.DoesNotContain(tools, tool => tool.Name == "harness_start_planning");
        string result = (await tools.Single(tool => tool.Name == "harness_application")
            .CallAsync()).Content.Single().ToString()!;
        Assert.Contains("instance", result, StringComparison.OrdinalIgnoreCase);
        Assert.Single(server.Current.ActiveClients);
        Assert.Equal("test-client", application.LastContext?.ClientId.Value);
        var created = await tools.Single(tool => tool.Name == "harness_create_goal").CallAsync(
            new Dictionary<string, object?>
            {
                ["expectedInstanceId"] = server.Current.InstanceId.Value,
                ["workspaceId"] = "workspace-a",
                ["title"] = "Dogfood lifecycle",
                ["objective"] = "Exercise the complete goal lifecycle through MCP.",
                ["reviewCycleLimit"] = 3,
                ["remoteBudgetMicrousd"] = null,
            });
        Assert.NotEqual(true, created.IsError);
        Assert.Equal("Dogfood lifecycle", application.LastCreate?.Title);
        var goals = await tools.Single(tool => tool.Name == "harness_goals").CallAsync(
            new Dictionary<string, object?>
            {
                ["goalId"] = "goal-a",
                ["maximumResults"] = 5,
                ["continuation"] = "10",
            });
        Assert.NotEqual(true, goals.IsError);
        Assert.Equal(new("goal-a", 5, "10"), application.LastGoalList);
        var models = await tools.Single(tool => tool.Name == "harness_goal_models").CallAsync(
            new Dictionary<string, object?>
            {
                ["goalId"] = "goal-a",
                ["provider"] = "Ollama",
                ["role"] = "Lead",
                ["search"] = "ornith",
                ["maximumResults"] = 25,
                ["continuation"] = null,
            });
        Assert.NotEqual(true, models.IsError);
        Assert.Equal(new("goal-a", "Ollama", "Lead", "ornith", 25, null),
            application.LastCatalog);
        var evidence = await tools.Single(tool => tool.Name == "harness_evidence").CallAsync(
            new Dictionary<string, object?>
            {
                ["goalId"] = "goal-a",
                ["maximumResults"] = 10,
                ["continuation"] = null,
            });
        Assert.NotEqual(true, evidence.IsError);
        Assert.Equal(new("goal-a", 10, null), application.LastEvidence);
        var workflowEvidence = await tools
            .Single(tool => tool.Name == "harness_workflow_evidence").CallAsync(
                new Dictionary<string, object?>
                {
                    ["goalId"] = "goal-a",
                    ["maximumResults"] = 7,
                    ["continuation"] = "3",
                });
        Assert.NotEqual(true, workflowEvidence.IsError);
        Assert.Equal(new("goal-a", 7, "3"), application.LastWorkflowEvidence);
        var stale = await tools.Single(tool => tool.Name == "harness_open_document").CallAsync(
            new Dictionary<string, object?>
            {
                ["expectedInstanceId"] = "stale-instance",
                ["relativePath"] = "README.md",
                ["goalId"] = null,
            });
        Assert.True(stale.IsError);
        Assert.Contains(audit.Records,
            item => item.ErrorCode == "stale_application_instance" &&
                    item.Outcome is InboundMcpAuditOutcome.Denied);
    }

    [Fact]
    public async Task Disabled_server_does_not_bind_and_token_rotation_revokes_clients()
    {
        int port = FreePort();
        StaticSettings settings = new(Settings(new($"http://127.0.0.1:{port}/mcp")) with
        { IsEnabled = false });
        MemorySecrets secrets = new("old");
        await using InboundMcpServer server = new(settings, secrets, new RecordingApplication(),
            new MemoryAuditStore(), NullLoggerFactory.Instance, TimeProvider.System);

        await server.ApplyAsync();
        Assert.False(server.Current.IsRunning);
        InboundMcpBearerToken replacement = await server.RotateTokenAsync();
        Assert.NotEqual("old", secrets.Value);
        Assert.Equal(secrets.Value, replacement.Value);
    }

    [Fact]
    public async Task Live_rotation_and_disconnect_revoke_authentication_immediately()
    {
        int port = FreePort();
        Uri endpoint = new($"http://127.0.0.1:{port}/mcp");
        StaticSettings settings = new(Settings(endpoint));
        MemorySecrets secrets = new("old-token");
        await using InboundMcpServer server = new(settings, secrets, new RecordingApplication(),
            new MemoryAuditStore(), NullLoggerFactory.Instance, TimeProvider.System);
        await server.ApplyAsync();

        Assert.NotEqual(HttpStatusCode.Unauthorized,
            (await SendAsync(endpoint, "old-token", "codex")).StatusCode);
        await server.DisconnectAsync(new("codex"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(endpoint, "old-token", "codex")).StatusCode);

        InboundMcpBearerToken replacement = await server.RotateTokenAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(endpoint, "old-token", "fresh-client")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized,
            (await SendAsync(endpoint, replacement.Value, "fresh-client")).StatusCode);
    }

    [Fact]
    public async Task Harness_control_connection_authenticates_and_exposes_only_exact_allowlist()
    {
        int port = FreePort();
        Uri endpoint = new($"http://127.0.0.1:{port}/mcp");
        MemorySecrets secrets = new("worker-token");
        await using InboundMcpServer server = new(
            new StaticSettings(Settings(endpoint)),
            secrets,
            new RecordingApplication(),
            new MemoryAuditStore(),
            NullLoggerFactory.Instance,
            TimeProvider.System);
        await server.ApplyAsync();
        McpConnectionConfiguration connection = new(
            new("worker"),
            new(endpoint),
            new(TimeSpan.FromSeconds(10)),
            IsEnabled: true,
            RequiresRestart: false,
            McpConnectionAccess.HarnessControl,
            new("controller"),
            new("worker-token-reference"),
            [new("harness_application"), new("harness_create_goal")]);
        await using StatelessHttpMcpToolClient client = new(
            new([connection]), secrets, NullLoggerFactory.Instance);

        McpConnectionDiscovery discovered = Assert.Single(
            (await client.DiscoverAsync()).Connections);

        Assert.Null(discovered.Error);
        Assert.Equal(
            ["harness_application", "harness_create_goal"],
            discovered.Tools.Where(tool => tool.IsAgentEligible)
                .Select(tool => tool.Name.Value));
        Assert.Contains(discovered.Tools, tool =>
            tool.Name.Value == "harness_abort_goal" && !tool.IsAgentEligible);
        McpToolInvocationResult invoked = await client.InvokeAsync(new(
            connection.Name, new("harness_application"),
            new Dictionary<string, object?>()));
        Assert.False(invoked.IsError);
        Assert.Contains("instance", invoked.Json, StringComparison.OrdinalIgnoreCase);
    }

    private static InboundMcpServerSettings Settings(Uri endpoint) => new(
        true, InboundMcpMode.Normal, endpoint, new("token"), [],
        [new("harness_application"), new("harness_workspace"), new("harness_tree"),
            new("harness_read_range"), new("harness_git"), new("harness_project_graph"),
            new("harness_goals"), new("harness_evidence"),
            new("harness_workflow_evidence"),
            new("harness_build"), new("harness_open_document"), new("harness_create_goal"),
            new("harness_goal_models"), new("harness_select_goal_model"),
            new("harness_start_planning"), new("harness_abort_goal"),
            new("harness_commit_preview"), new("harness_code_inspection")],
        [new("harness_build"), new("harness_start_planning")],
        new(TimeSpan.FromSeconds(10)), new(100), new(100), false);

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Uri endpoint, string token, string client)
    {
        HttpClient http = new();
        HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("X-Harness-Client", client);
        HttpResponseMessage response = await http.SendAsync(request);
        http.Dispose();
        return response;
    }

    private sealed class StaticSettings(InboundMcpServerSettings value) : IInboundMcpSettingsStore
    {
        public ValueTask<InboundMcpServerSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(value);
        public ValueTask<InboundMcpServerSettings> SaveAsync(
            InboundMcpServerSettings settings, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(settings);
    }

    private sealed class MemorySecrets(string? value) : ISecretStore
    {
        public string? Value { get; private set; } = value;
        public ValueTask<string?> GetAsync(SecretReference reference,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Value);
        public ValueTask SetAsync(SecretReference reference, string value,
            CancellationToken cancellationToken = default)
        { Value = value; return ValueTask.CompletedTask; }
    }

    private sealed class MemoryAuditStore : IInboundMcpAuditStore
    {
        private readonly List<InboundMcpAuditRecord> records = [];
        public IReadOnlyList<InboundMcpAuditRecord> Records => records;

        public ValueTask AppendAsync(InboundMcpAuditRecord record, int retention,
            CancellationToken cancellationToken = default)
        {
            records.Add(record);
            if (records.Count > retention)
                records.RemoveRange(0, records.Count - retention);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<InboundMcpAuditRecord>> ListAsync(
            int maximumResults, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<InboundMcpAuditRecord>>(
                records.TakeLast(maximumResults).Reverse().ToArray());
    }

    private sealed class RecordingApplication : IInboundMcpApplication
    {
        public InboundMcpCallContext? LastContext { get; private set; }
        public InboundMcpGoalCreateRequest? LastCreate { get; private set; }
        public InboundMcpGoalListRequest? LastGoalList { get; private set; }
        public InboundMcpGoalCatalogRequest? LastCatalog { get; private set; }
        public InboundMcpEvidenceRequest? LastEvidence { get; private set; }
        public InboundMcpWorkflowEvidenceRequest? LastWorkflowEvidence { get; private set; }
        public ValueTask<InboundMcpApplicationResult> GetApplicationAsync(
            InboundMcpCallContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context; return ValueTask.FromResult(new InboundMcpApplicationResult(
              $"{{\"instance\":\"{context.InstanceId.Value}\"}}", false, null, null));
        }
        public ValueTask<InboundMcpApplicationResult> GetEvaluationSnapshotAsync(
            InboundMcpCallContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ResetEvaluationAsync(
            InboundMcpCallContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> GetWorkspaceAsync(InboundMcpCallContext context,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ListTreeAsync(InboundMcpCallContext context,
            InboundMcpTreeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ReadRangeAsync(InboundMcpCallContext context,
            InboundMcpRangeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> GetGitAsync(InboundMcpCallContext context,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> GetProjectGraphAsync(InboundMcpCallContext context,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ListGoalsAsync(InboundMcpCallContext context,
            InboundMcpGoalListRequest request,
            CancellationToken cancellationToken = default)
        {
            LastGoalList = request;
            return ValueTask.FromResult(new InboundMcpApplicationResult(
                "{\"goals\":[]}", false, null, null));
        }
        public ValueTask<InboundMcpApplicationResult> ListEvidenceAsync(InboundMcpCallContext context,
            InboundMcpEvidenceRequest request, CancellationToken cancellationToken = default)
        {
            LastEvidence = request;
            return ValueTask.FromResult(new InboundMcpApplicationResult(
                "{\"evidence\":[]}", false, null, null));
        }
        public ValueTask<InboundMcpApplicationResult> ListWorkflowEvidenceAsync(
            InboundMcpCallContext context, InboundMcpWorkflowEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            LastWorkflowEvidence = request;
            return ValueTask.FromResult(new InboundMcpApplicationResult(
                "{\"evidence\":[]}", false, null, null));
        }
        public ValueTask<InboundMcpApplicationResult> CreateGoalAsync(InboundMcpCallContext context,
            InboundMcpGoalCreateRequest request, CancellationToken cancellationToken = default)
        {
            LastCreate = request;
            return ValueTask.FromResult(new InboundMcpApplicationResult(
                "{\"goal\":\"created\"}", false, null, null));
        }
        public ValueTask<InboundMcpApplicationResult> UpdateGoalSettingsAsync(InboundMcpCallContext context,
            InboundMcpGoalSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ExtendGoalBudgetAsync(InboundMcpCallContext context,
            InboundMcpGoalBudgetRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> DiscoverGoalModelsAsync(InboundMcpCallContext context,
            InboundMcpGoalCatalogRequest request, CancellationToken cancellationToken = default)
        {
            LastCatalog = request;
            return ValueTask.FromResult(new InboundMcpApplicationResult(
                "{\"models\":[]}", false, null, null));
        }
        public ValueTask<InboundMcpApplicationResult> SelectGoalModelAsync(InboundMcpCallContext context,
            InboundMcpGoalModelRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> StartGoalPlanningAsync(InboundMcpCallContext context,
            InboundMcpGoalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ResumeGoalAsync(InboundMcpCallContext context,
            InboundMcpGoalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> RetryGoalAsync(InboundMcpCallContext context,
            InboundMcpGoalRetryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> AbortGoalAsync(InboundMcpCallContext context,
            InboundMcpGoalAbortRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> CancelGoalOperationAsync(InboundMcpCallContext context,
            InboundMcpGoalOperationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> DecidePlanAsync(InboundMcpCallContext context,
            InboundMcpPlanDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> BuildAsync(InboundMcpCallContext context,
            InboundMcpExecutionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> TestAsync(InboundMcpCallContext context,
            InboundMcpExecutionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> PreviewCommitAsync(InboundMcpCallContext context,
            InboundMcpGoalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> RequestCommitApprovalAsync(InboundMcpCallContext context,
            InboundMcpCommitApprovalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> DecideCommitAsync(InboundMcpCallContext context,
            InboundMcpCommitDecisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> GetUiAsync(InboundMcpCallContext context,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> ActivateUiAsync(InboundMcpCallContext context,
            InboundMcpUiActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> OpenDocumentAsync(InboundMcpCallContext context,
            InboundMcpOpenDocumentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> RequestCaptureAsync(InboundMcpCallContext context,
            InboundMcpCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> InspectCaptureAsync(InboundMcpCallContext context,
            InboundMcpCaptureInspectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> InspectCodeProblemsAsync(InboundMcpCallContext context,
            InboundMcpCodeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> GetCodeSymbolAsync(InboundMcpCallContext context,
            InboundMcpCodePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> FindCodeDefinitionAsync(InboundMcpCallContext context,
            InboundMcpCodePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> FindCodeReferencesAsync(InboundMcpCallContext context,
            InboundMcpCodePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<InboundMcpApplicationResult> FindCodeImplementationsAsync(InboundMcpCallContext context,
            InboundMcpCodePositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
