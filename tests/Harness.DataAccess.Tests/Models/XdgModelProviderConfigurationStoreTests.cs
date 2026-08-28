using System.Xml.Linq;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Models.Configuration;
using Harness.DataAccess.Secrets;

namespace Harness.DataAccess.Tests.Models;

public sealed class XdgModelProviderConfigurationStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-provider-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Saves_a_minimal_provider_override_without_replacing_unrelated_configuration()
    {
        StubApplicationPaths paths = new(CreatePaths());
        Directory.CreateDirectory(paths.Current.ConfigDirectory);
        string path = Path.Combine(paths.Current.ConfigDirectory, "harness.xml");
        await File.WriteAllTextAsync(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <Harness>
              <Routing><MainLlm>Ollama</MainLlm></Routing>
              <Providers><Ollama><Existing>preserve</Existing></Ollama></Providers>
            </Harness>
            """);
        XdgModelProviderConfigurationStore store = new(paths, new([
            Configuration("Ollama", StoredModelProviderKind.Ollama),
        ]));

        StoredModelProviderConfiguration saved = await store.SaveAsync(
            Configuration("Ollama", StoredModelProviderKind.Ollama) with
            {
                Endpoint = new(new("http://localhost:11434")),
                ChatModel = new("qwen3:latest"),
                EmbeddingDimensions = new(1024),
            });

        XDocument document = XDocument.Load(path);
        Assert.Equal("Ollama", document.Root?.Element("Routing")?.Element("MainLlm")?.Value);
        XElement provider = Assert.IsType<XElement>(document.Root?
            .Element("Providers")?.Element("Ollama"));
        Assert.Equal("preserve", provider.Element("Existing")?.Value);
        Assert.Equal("http://localhost:11434", provider.Element("Endpoint")?.Value);
        Assert.Equal("qwen3:latest", provider.Element("ChatModel")?.Value);
        Assert.Equal("1024", provider.Element("EmbeddingDimensions")?.Value);
        Assert.Equal("8192", provider.Element("MaximumAgentContextTokens")?.Value);
        Assert.True(saved.RequiresRestart);
        Assert.True((await store.ListAsync()).Single().RequiresRestart);
    }

    [Fact]
    public async Task Writes_secret_references_but_never_a_credential_value()
    {
        StubApplicationPaths paths = new(CreatePaths());
        XdgModelProviderConfigurationStore store = new(paths, new([
            Configuration("OpenRouter", StoredModelProviderKind.OpenRouter) with
            {
                ApiKeyReference = new("openrouter-key", "OPENROUTER_API_KEY"),
            },
        ]));

        await store.SaveAsync(Configuration("OpenRouter", StoredModelProviderKind.OpenRouter) with
        {
            ApiKeyReference = new("replacement-key", "OPENROUTER_TOKEN"),
        });

        string content = await File.ReadAllTextAsync(
            Path.Combine(paths.Current.ConfigDirectory, "harness.xml"));
        Assert.Contains("replacement-key", content, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_TOKEN", content, StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret-value", content, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private StoredModelProviderConfiguration Configuration(
        string name,
        StoredModelProviderKind kind) => new(
        new(name),
        kind,
        new(new Uri(kind is StoredModelProviderKind.Ollama
            ? "http://localhost:11434"
            : "https://openrouter.ai")),
        new("chat"),
        new("embedding"),
        new(768),
        kind is StoredModelProviderKind.Ollama ? new(8_192) : null,
        new(TimeSpan.FromSeconds(5)),
        new(TimeSpan.FromSeconds(600)),
        ApiKeyReference: null,
        RequiresRestart: false);

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(testDirectory, "config"),
        Path.Combine(testDirectory, "data"),
        Path.Combine(testDirectory, "state"),
        Path.Combine(testDirectory, "cache"),
        Path.Combine(testDirectory, "data", "harness.db"),
        Path.Combine(testDirectory, "state", "logs"),
        Path.Combine(testDirectory, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
