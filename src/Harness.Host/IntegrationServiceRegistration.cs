using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Research;
using Harness.DataAccess.Mcp;
using Harness.DataAccess.Research;
using Harness.Host.Configuration;
using Harness.Presentation.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Harness.Host;

internal static class IntegrationServiceRegistration
{
    internal static IServiceCollection AddHarnessIntegrations(
        this IServiceCollection services,
        HarnessConfiguration configuration,
        bool isEvaluation)
    {
        services.AddSingleton(new McpConnectionConfigurationOptions(
            configuration.McpConnections.Select(connection => new Harness.DataAccess.Mcp.McpConnectionConfiguration(
                new(connection.Name),
                new(connection.Endpoint),
                new(connection.RequestTimeout),
                connection.IsEnabled,
                RequiresRestart: false,
                Access: connection.Access is McpConnectionAccessKind.HarnessControl
                    ? McpConnectionAccess.HarnessControl
                    : McpConnectionAccess.ReadOnly,
                ClientId: connection.ClientId is null ? null : new(connection.ClientId),
                AllowedTools: connection.AllowedTools.Select(tool =>
                    new McpToolName(tool)).ToArray())).ToArray()));
        services.AddSingleton<IMcpConnectionConfigurationStore, XdgMcpConnectionConfigurationStore>();
        services.AddSingleton<IMcpToolClient, StatelessHttpMcpToolClient>();
        services.AddSingleton<IMcpSettingsService, McpSettingsService>();
        services.AddSingleton<IMcpToolService, McpToolService>();
        services.AddSingleton<IInboundMcpSettingsStore, XdgInboundMcpSettingsStore>();
        services.AddSingleton<IInboundMcpAuditStore, FileInboundMcpAuditStore>();
        services.AddSingleton<IInboundMcpEvaluationFixture, InboundMcpEvaluationFixture>();
        services.AddSingleton(new InboundMcpApplicationEnvironment(isEvaluation));
        services.AddSingleton<AvaloniaInboundMcpUiBridge>();
        services.AddSingleton<IInboundMcpUiBridge>(provider =>
            provider.GetRequiredService<AvaloniaInboundMcpUiBridge>());
        services.AddSingleton<IInboundGoalOperationCoordinator, InboundGoalOperationCoordinator>();
        services.AddSingleton<IInboundMcpApplication, InboundMcpApplicationService>();
        services.AddSingleton<InboundMcpServer>();
        services.AddSingleton<IInboundMcpRuntime>(provider =>
            provider.GetRequiredService<InboundMcpServer>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<InboundMcpServer>());
        services.AddSingleton<IInboundMcpSettingsService, InboundMcpSettingsService>();

        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders =
            {
                { "User-Agent", "Harness.NET/1.0 documentation-dependency-research" },
            },
        });
        services.AddSingleton<IResearchSettingsStore, XdgResearchSettingsStore>();
        services.AddSingleton<IDocumentationCache, FileDocumentationCache>();
        services.AddSingleton<IDocumentationSource, LocalPackageDocumentationSource>();
        services.AddSingleton<IDocumentationSource, LocalDocumentationIndexSource>();
        services.AddSingleton<IDocumentationSource, McpDocumentationSource>();
        services.AddSingleton<IDocumentationSource, HttpDocumentationSource>();
        services.AddSingleton<IDependencyEvidenceReader, DependencyEvidenceReader>();
        services.AddSingleton<IPackageCandidateMetadataClient, NuGetPackageCandidateMetadataClient>();
        services.AddSingleton<ISbomExporter, AtomicSbomExporter>();
        services.AddSingleton<ResearchWorkspaceResolver>();
        services.AddSingleton<IDocumentationResearchService, DocumentationResearchService>();
        services.AddSingleton<IDependencyResearchService, DependencyResearchService>();
        services.AddSingleton<IResearchSettingsService, ResearchSettingsService>();
        return services;
    }
}
