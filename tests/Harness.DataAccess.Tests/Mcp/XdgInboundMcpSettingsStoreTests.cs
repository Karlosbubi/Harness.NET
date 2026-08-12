using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mcp;

namespace Harness.DataAccess.Tests.Mcp;

public sealed class XdgInboundMcpSettingsStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "harness-inbound-mcp-settings",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Defaults_disabled_and_round_trips_private_configuration()
    {
        XdgInboundMcpSettingsStore store = new(new Paths(root));
        InboundMcpServerSettings defaults = await store.GetAsync();
        Assert.False(defaults.IsEnabled);
        Assert.True(defaults.Endpoint.IsLoopback);
        Assert.Contains(defaults.AllowedTools,
            tool => tool.Value == "harness_goal_models");
        Assert.Contains(defaults.AllowedTools,
            tool => tool.Value == "harness_commit_preview");
        Assert.Contains(defaults.AllowedTools,
            tool => tool.Value == "harness_workflow_evidence");
        Assert.Contains(defaults.AllowedTools,
            tool => tool.Value == "harness_code_actions");

        InboundMcpServerSettings saved = await store.SaveAsync(defaults with
        {
            IsEnabled = true,
            Mode = InboundMcpMode.IsolatedEvaluation,
            AllowedClients = [new("codex")],
            AllowedTools = [.. defaults.AllowedTools, new("harness_create_goal")],
        });
        InboundMcpServerSettings loaded = await store.GetAsync();

        Assert.True(saved.RequiresRestart);
        Assert.True(loaded.IsEnabled);
        Assert.Equal(InboundMcpMode.IsolatedEvaluation, loaded.Mode);
        Assert.Equal("codex", Assert.Single(loaded.AllowedClients).Value);
        Assert.Contains(loaded.AllowedTools,
            tool => tool.Value == "harness_create_goal");
    }

    [Fact]
    public async Task Rejects_non_loopback_binding()
    {
        XdgInboundMcpSettingsStore store = new(new Paths(root));
        InboundMcpServerSettings defaults = await store.GetAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(defaults with
        { Endpoint = new Uri("http://0.0.0.0:57431/mcp") }).AsTask());
    }

    [Fact]
    public async Task Rejects_unknown_tool_ids_and_invalid_client_ids()
    {
        XdgInboundMcpSettingsStore store = new(new Paths(root));
        InboundMcpServerSettings defaults = await store.GetAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(defaults with
        { AllowedTools = [new("run_anything")] }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(defaults with
        { AllowedClients = [new(string.Empty)] }).AsTask());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class Paths(string root) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = new(root, root, root, root,
            Path.Combine(root, "harness.db"), Path.Combine(root, "logs"), Path.Combine(root, "worktrees"));
    }
}
