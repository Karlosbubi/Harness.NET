using Harness.DataAccess.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Harness.DataAccess.Tests.Observability;

public sealed class ObservabilityBootstrapTests : IDisposable
{
    private readonly string logDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_json_logs_and_redacts_sensitive_properties()
    {
        ServiceCollection services = new();
        ObservabilityBootstrap.Configure(services, new(logDirectory, OtlpEndpoint: null));

        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            logger.LogInformation(
                "Connected {Workspace} using {ApiKey}",
                "sample-repository",
                "do-not-write-this-secret");
        }

        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(logDirectory, "*.jsonl")));
        Assert.Contains("sample-repository", log, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-write-this-secret", log, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }
}
