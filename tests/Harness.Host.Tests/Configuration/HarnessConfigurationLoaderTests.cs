using Harness.DataAccess.Configuration;
using Harness.Host.Configuration;

namespace Harness.Host.Tests.Configuration;

public sealed class HarnessConfigurationLoaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"harness-host-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Loads_shipped_defaults()
    {
        HarnessConfiguration configuration = Load();

        ModelProviderConfiguration provider = configuration.Providers["Ollama"];
        Assert.Equal("Ollama", configuration.Routing.MainLlm);
        Assert.Equal(new Uri("http://192.168.1.101:11434"), provider.Endpoint);
        Assert.Equal("gemma4:latest", provider.ChatModel);
        Assert.Equal(TimeSpan.FromMinutes(10), provider.RequestTimeout);
    }

    [Fact]
    public void Xdg_configuration_overrides_shipped_defaults()
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(
            Path.Combine(ConfigDirectory, "harness.xml"),
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <Harness>
              <Providers>
                <Ollama>
                  <Endpoint>http://localhost:11434</Endpoint>
                  <ChatModel>test-model</ChatModel>
                </Ollama>
              </Providers>
            </Harness>
            """);

        HarnessConfiguration configuration = Load();

        Assert.Equal(
            new Uri("http://localhost:11434"),
            configuration.Providers["Ollama"].Endpoint);
        Assert.Equal("test-model", configuration.Providers["Ollama"].ChatModel);
    }

    [Fact]
    public void Command_line_overrides_xml_configuration()
    {
        HarnessConfiguration configuration = Load(
            "--Providers:Ollama:ChatModel=argument-model",
            "--Conversation:Id=argument-conversation");

        Assert.Equal("argument-model", configuration.Providers["Ollama"].ChatModel);
        Assert.Equal("argument-conversation", configuration.Conversation.Id);
    }

    [Fact]
    public void Rejects_route_to_unknown_provider()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Load("--Routing:MainLlm=Missing"));

        Assert.Contains("unknown provider 'Missing'", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string ConfigDirectory => Path.Combine(root, "config");

    private HarnessConfiguration Load(params string[] args)
    {
        Directory.CreateDirectory(root);
        ApplicationPaths paths = new(
            ConfigDirectory,
            Path.Combine(root, "data"),
            Path.Combine(root, "state"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"),
            Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));

        return HarnessConfigurationLoader.Load(args, paths, AppContext.BaseDirectory);
    }
}
