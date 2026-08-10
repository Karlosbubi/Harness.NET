using System.Xml.Linq;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mcp;

namespace Harness.DataAccess.Tests.Mcp;

public sealed class XdgMcpConnectionConfigurationStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"harness-mcp-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task Adds_updates_and_removes_connection_without_replacing_other_settings()
    {
        StubApplicationPaths paths = new(Paths());
        Directory.CreateDirectory(paths.Current.ConfigDirectory);
        string path = Path.Combine(paths.Current.ConfigDirectory, "harness.xml");
        await File.WriteAllTextAsync(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <Harness><Routing><MainLlm>Ollama</MainLlm></Routing></Harness>
            """);
        XdgMcpConnectionConfigurationStore store = new(paths, new([]));

        McpConnectionConfiguration saved = await store.SaveAsync(new(
            new("docs"),
            new(new Uri("https://docs.example.test/mcp")),
            new(TimeSpan.FromSeconds(45)),
            IsEnabled: true,
            RequiresRestart: false));

        XDocument document = XDocument.Load(path);
        Assert.Equal("Ollama", document.Root?.Element("Routing")?.Element("MainLlm")?.Value);
        XElement connection = Assert.IsType<XElement>(document.Root?
            .Element("McpConnections")?.Element("docs"));
        Assert.Equal("https://docs.example.test/mcp", connection.Element("Endpoint")?.Value);
        Assert.Equal("45", connection.Element("RequestTimeoutSeconds")?.Value);
        Assert.Equal("True", connection.Element("Enabled")?.Value);
        Assert.True(saved.RequiresRestart);

        Assert.True(await store.DeleteAsync(new("docs")));
        document = XDocument.Load(path);
        Assert.Null(document.Root?.Element("McpConnections")?.Element("docs"));
        Assert.Equal("Ollama", document.Root?.Element("Routing")?.Element("MainLlm")?.Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ApplicationPaths Paths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
