using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Json;

namespace Harness.DataAccess.Observability;

internal static class ObservabilityBootstrap
{
    private const string ServiceName = "Harness.NET";
    private const string LogFileName = "harness-.jsonl";

    internal static void Configure(
        IServiceCollection services,
        ObservabilityOptions options)
    {
        Directory.CreateDirectory(options.LogDirectory);

        Serilog.Core.Logger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.File(
                new JsonFormatter(renderMessage: true),
                Path.Combine(options.LogDirectory, LogFileName),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 14)
            .CreateLogger();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(logger, dispose: true);
        });

        if (options.OtlpEndpoint is not null)
        {
            ConfigureOpenTelemetry(services, options.OtlpEndpoint);
        }
    }

    private static void ConfigureOpenTelemetry(IServiceCollection services, Uri endpoint)
    {
        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(ServiceName)
                .AddOtlpExporter(exporter => exporter.Endpoint = endpoint))
            .WithMetrics(metrics => metrics
                .AddMeter(ServiceName)
                .AddOtlpExporter(exporter => exporter.Endpoint = endpoint));
    }
}
