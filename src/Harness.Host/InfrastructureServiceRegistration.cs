using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Privacy;
using Harness.DataAccess.Appearance;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Editor;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Models.Configuration;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Secrets;
using Harness.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Harness.Host;

internal static class InfrastructureServiceRegistration
{
    internal static IServiceCollection AddHarnessInfrastructure(
        this IServiceCollection services,
        IApplicationPaths applicationPaths,
        HarnessConfiguration configuration,
        string? evaluationRoot)
    {
        services.AddSingleton(applicationPaths);
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IApplicationBackup, SqliteApplicationBackup>();
        services.AddSingleton<IApplicationRestore, SqliteApplicationRestore>();
        services.AddSingleton<IApplicationOperationsService, ApplicationOperationsService>();
        services.AddSingleton<IAppearancePreferenceStore, SqliteAppearancePreferenceStore>();
        services.AddSingleton<IEditorIntelligencePreferenceStore,
            SqliteEditorIntelligencePreferenceStore>();
        services.AddSingleton<IKeybindingPreferenceStore, SqliteKeybindingPreferenceStore>();
        services.AddSingleton<IRemoteSpendPreferenceStore, SqliteRemoteSpendPreferenceStore>();
        services.AddSingleton<IUserThemeSource, XdgUserThemeSource>();

        if (evaluationRoot is null)
        {
            services.AddSingleton<ISecretStore, SecretServiceSecretStore>();
        }
        else
        {
            services.AddSingleton<VolatileSecretStore>();
            services.AddSingleton<ISecretStore>(provider =>
                provider.GetRequiredService<VolatileSecretStore>());
        }

        services.AddSingleton(new ModelProviderConfigurationOptions(
            configuration.Providers.Values.Select(provider => new StoredModelProviderConfiguration(
                new(provider.Name),
                provider.Kind is ModelProviderKind.Ollama
                    ? StoredModelProviderKind.Ollama
                    : StoredModelProviderKind.OpenRouter,
                new(provider.Endpoint),
                new(provider.ChatModel),
                new(provider.EmbeddingModel),
                new(provider.EmbeddingDimensions),
                new(provider.ConnectTimeout),
                new(provider.RequestTimeout),
                provider.ApiKeyReference,
                RequiresRestart: false)).ToArray()));
        services.AddSingleton<IModelProviderConfigurationStore, XdgModelProviderConfigurationStore>();
        services.AddSingleton<IModelProviderSettingsService, ModelProviderSettingsService>();
        return services;
    }
}
