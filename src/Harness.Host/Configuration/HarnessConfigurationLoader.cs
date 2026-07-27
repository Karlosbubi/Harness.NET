using Harness.DataAccess.Configuration;
using Microsoft.Extensions.Configuration;

namespace Harness.Host.Configuration;

internal static class HarnessConfigurationLoader
{
    internal static HarnessConfiguration Load(
        string[] args,
        ApplicationPaths applicationPaths,
        string baseDirectory)
    {
        string defaultConfigurationPath = Path.Combine(baseDirectory, "harness.xml");
        string userConfigurationPath = Path.Combine(applicationPaths.ConfigDirectory, "harness.xml");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddXmlFile(defaultConfigurationPath, optional: false, reloadOnChange: false)
            .AddXmlFile(userConfigurationPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("HARNESS_")
            .AddCommandLine(args.Where(static argument => argument != "--no-ui").ToArray())
            .Build();

        IReadOnlyDictionary<string, ModelProviderConfiguration> providers = configuration
            .GetRequiredSection("Providers")
            .GetChildren()
            .Select(ParseProvider)
            .ToDictionary(static provider => provider.Name, StringComparer.OrdinalIgnoreCase);

        ProviderRoutingConfiguration routing = new(
            Required(configuration, "Routing:MainLlm"),
            Required(configuration, "Routing:Reviewer"),
            Required(configuration, "Routing:ToolLlm"));

        ValidateRoute(providers, nameof(routing.MainLlm), routing.MainLlm);
        ValidateRoute(providers, nameof(routing.Reviewer), routing.Reviewer);
        ValidateRoute(providers, nameof(routing.ToolLlm), routing.ToolLlm);

        return new(
            providers,
            routing,
            new(
                Required(configuration, "Conversation:Id"),
                Required(configuration, "Conversation:Title"),
                Path.GetFullPath(Required(configuration, "Conversation:WorkspacePath"))),
            new(ParseOptionalUri(configuration["Observability:OtlpEndpoint"])),
            ParseFramework(configuration));
    }

    private static FrameworkConfiguration ParseFramework(IConfiguration configuration)
    {
        FrameworkRuleConfiguration[] rules = configuration
            .GetSection("Framework:Rules")
            .GetChildren()
            .Select(section => new FrameworkRuleConfiguration(
                section.Key,
                Required(section, "Value"),
                RequiredNonNegativeInt(section, "Precedence"),
                Required(section, "Layer"),
                ParseBoolean(section, "Locked"),
                $"harness.xml:Framework:Rules:{section.Key}"))
            .ToArray();
        return new(rules);
    }

    private static ModelProviderConfiguration ParseProvider(IConfigurationSection section)
    {
        string kind = Required(section, "Kind");
        if (!kind.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Provider '{section.Key}' has unsupported kind '{kind}'.");
        }

        return new(
            section.Key,
            kind,
            RequiredUri(section, "Endpoint"),
            Required(section, "ChatModel"),
            Required(section, "EmbeddingModel"),
            TimeSpan.FromSeconds(RequiredPositiveInt(section, "ConnectTimeoutSeconds")),
            TimeSpan.FromSeconds(RequiredPositiveInt(section, "RequestTimeoutSeconds")));
    }

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Configuration value '{key}' is required.")
            : configuration[key]!;

    private static Uri RequiredUri(IConfiguration configuration, string key)
    {
        string value = Required(configuration, key);
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new InvalidOperationException($"Configuration value '{key}' must be an absolute URI.");
    }

    private static Uri? ParseOptionalUri(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                ? uri
                : throw new InvalidOperationException(
                    "Configuration value 'Observability:OtlpEndpoint' must be an absolute URI.");

    private static int RequiredPositiveInt(IConfiguration configuration, string key)
    {
        string value = Required(configuration, key);
        return int.TryParse(value, out int parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{key}' must be a positive integer.");
    }

    private static int RequiredNonNegativeInt(IConfiguration configuration, string key)
    {
        string value = Required(configuration, key);
        return int.TryParse(value, out int parsed) && parsed >= 0
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{key}' must be a non-negative integer.");
    }

    private static bool ParseBoolean(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? false
            : bool.TryParse(value, out bool parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Configuration value '{key}' must be true or false.");
    }

    private static void ValidateRoute(
        IReadOnlyDictionary<string, ModelProviderConfiguration> providers,
        string role,
        string providerName)
    {
        if (!providers.ContainsKey(providerName))
        {
            throw new InvalidOperationException(
                $"Routing role '{role}' references unknown provider '{providerName}'.");
        }
    }
}
