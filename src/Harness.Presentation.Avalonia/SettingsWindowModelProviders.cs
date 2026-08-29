using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;

namespace Harness.Presentation.Avalonia;

internal sealed partial class SettingsWindow
{
    private Control ModelProvidersPage()
    {
        AgentDefaultsSnapshot? snapshot = settingsState.AgentDefaults;
        Button refresh = new()
        {
            Content = "Refresh provider catalogs",
            IsEnabled = !settingsState.IsBusy,
        };
        refresh.Classes.Add("command");
        AutomationProperties.SetName(refresh, "Refresh Ollama and OpenRouter model catalogs");
        refresh.Click += async (_, _) => await store.DiscoverAgentDefaultsAsync(cancellationToken);

        StackPanel providers = new() { Spacing = 12 };
        if (snapshot is null)
        {
            providers.Children.Add(new TextBlock
            {
                Text = "Detecting configured model providers…",
                Classes = { "muted" },
            });
        }
        else
        {
            ModelProviderName[] providerNames = snapshot.Providers
                .Select(item => item.Provider)
                .Concat(settingsState.ProviderSettings?.Providers.Select(item => item.Provider) ?? [])
                .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (ModelProviderName providerName in providerNames)
            {
                ModelProviderSettingsView? configuration = settingsState.ProviderSettings?.Providers
                    .FirstOrDefault(item => item.Provider.Value.Equals(
                        providerName.Value,
                        StringComparison.OrdinalIgnoreCase));
                AgentModelProviderStatus provider = snapshot.Providers.FirstOrDefault(item =>
                    item.Provider.Value.Equals(providerName.Value, StringComparison.OrdinalIgnoreCase)) ?? new(
                    providerName,
                    configuration?.Kind is AgentModelProviderKind.OpenRouter
                        ? ModelAccess.Remote
                        : ModelAccess.Local,
                    configuration?.ChatModel ?? new("unknown"),
                    DiscoveredChatModels: 0,
                    RoleCompatibleModels: 0,
                    HasPublishedPricing: false,
                    AgentModelProviderAvailability.Empty,
                    "No catalog status is available.");
                providers.Children.Add(ProviderCard(provider, configuration));
            }
        }

        return Page(
            "Model providers",
            "Configure named Ollama and OpenRouter modules. Catalog discovery runs without inference; endpoint, model, and local context changes are written to your private XDG configuration and apply after restart.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new Border
                    {
                        Classes = { "card", "attention" },
                        Child = new TextBlock
                        {
                            Text = "Saved values update the private XDG override. Environment and command-line overrides retain higher precedence. Provider routes and embedding partition identity change only after restart.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    refresh,
                    providers,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                },
            });
    }

    private Control ProviderCard(
        AgentModelProviderStatus provider,
        ModelProviderSettingsView? configuration)
    {
        string pricing = provider.Access is ModelAccess.Remote
            ? provider.HasPublishedPricing
                ? "Published pricing available for discovered models"
                : "Published pricing unavailable; remote execution remains fail-closed"
            : "Local execution; no remote spending authority";
        StackPanel content = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = provider.Provider.Value,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = $"{provider.Access} · {provider.Availability} · configured default {provider.ConfiguredDefaultModel.Value}",
                    Classes = { "muted" },
                },
                new TextBlock
                {
                    Text = $"{provider.DiscoveredChatModels} chat model(s) · " +
                           $"{provider.RoleCompatibleModels} fully role-compatible · {pricing}",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = provider.Message ?? "Catalog ready.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                },
            },
        };

        if (configuration is null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Configuration controls are unavailable in this host.",
                Classes = { "muted" },
            });
            return new Border { Classes = { "card", "row" }, Child = content };
        }

        TextBox endpoint = ProviderTextBox(configuration.Endpoint.Value,
            $"{provider.Provider.Value} endpoint");
        TextBox chatModel = ProviderTextBox(configuration.ChatModel.Value,
            $"{provider.Provider.Value} default chat model");
        TextBox embeddingModel = ProviderTextBox(configuration.EmbeddingModel.Value,
            $"{provider.Provider.Value} default embedding model");
        NumericUpDown dimensions = ProviderNumber(
            configuration.EmbeddingDimensions.Value, 1, 65_536,
            $"{provider.Provider.Value} embedding dimensions");
        NumericUpDown? maximumAgentContext = configuration.Kind is AgentModelProviderKind.Ollama
            ? ProviderNumber(
                configuration.MaximumAgentContextTokens?.Value ?? 8_192,
                2_048,
                262_144,
                $"{provider.Provider.Value} maximum agent context tokens")
            : null;
        NumericUpDown connectTimeout = ProviderNumber(
            configuration.ConnectTimeout.Value, 1, 3_600,
            $"{provider.Provider.Value} connect timeout seconds");
        NumericUpDown requestTimeout = ProviderNumber(
            configuration.RequestTimeout.Value, 1, 3_600,
            $"{provider.Provider.Value} request timeout seconds");
        TextBox? secretName = configuration.Kind is AgentModelProviderKind.OpenRouter
            ? ProviderTextBox(configuration.SecretName?.Value ?? string.Empty,
                $"{provider.Provider.Value} Secret Service key name")
            : null;
        TextBox? environmentVariable = configuration.Kind is AgentModelProviderKind.OpenRouter
            ? ProviderTextBox(configuration.EnvironmentVariable?.Value ?? string.Empty,
                $"{provider.Provider.Value} API key environment variable")
            : null;
        Button save = new()
        {
            Content = "Save provider configuration",
            IsEnabled = !settingsState.IsBusy,
        };
        save.Classes.Add("command");
        AutomationProperties.SetName(save, $"Save {provider.Provider.Value} provider configuration");
        save.Click += async (_, _) => await store.UpdateModelProviderAsync(new(
            configuration.Provider,
            new(endpoint.Text ?? string.Empty),
            new(chatModel.Text ?? string.Empty),
            new(embeddingModel.Text ?? string.Empty),
            new(decimal.ToInt32(dimensions.Value ?? 0)),
            maximumAgentContext is null
                ? null
                : new(decimal.ToInt32(maximumAgentContext.Value ?? 0)),
            new(decimal.ToInt32(connectTimeout.Value ?? 0)),
            new(decimal.ToInt32(requestTimeout.Value ?? 0)),
            secretName is null ? null : new(secretName.Text ?? string.Empty),
            environmentVariable is null || string.IsNullOrWhiteSpace(environmentVariable.Text)
                ? null
                : new(environmentVariable.Text)), cancellationToken);

        Grid fields = new()
        {
            ColumnDefinitions = new("*,*"),
            RowDefinitions = new(maximumAgentContext is null
                ? "Auto,Auto,Auto"
                : "Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        AddProviderField(fields, 0, 0, "Endpoint", endpoint);
        AddProviderField(fields, 0, 1, "Default chat model", chatModel);
        AddProviderField(fields, 1, 0, "Default embedding model", embeddingModel);
        AddProviderField(fields, 1, 1, "Embedding dimensions", dimensions);
        if (maximumAgentContext is not null)
        {
            AddProviderField(fields, 2, 0, "Maximum agent context (tokens)", maximumAgentContext);
        }
        int timeoutRow = maximumAgentContext is null ? 2 : 3;
        AddProviderField(fields, timeoutRow, 0, "Connect timeout (seconds)", connectTimeout);
        AddProviderField(fields, timeoutRow, 1, "Request timeout (seconds)", requestTimeout);
        content.Children.Add(new Separator());
        content.Children.Add(fields);
        if (maximumAgentContext is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "The adapter sizes shorter requests down automatically. This ceiling bounds local KV-cache memory and is also limited by the selected model.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (secretName is not null && environmentVariable is not null)
        {
            Grid secretReferences = new()
            {
                ColumnDefinitions = new("*,*"),
                ColumnSpacing = 10,
            };
            AddProviderField(secretReferences, 0, 0, "Secret Service key", secretName);
            AddProviderField(secretReferences, 0, 1, "Environment fallback", environmentVariable);
            content.Children.Add(secretReferences);

            TextBox credential = new()
            {
                PasswordChar = '●',
                PlaceholderText = "Paste a new API key",
                MinWidth = 280,
                IsEnabled = !settingsState.IsBusy,
            };
            AutomationProperties.SetName(credential, $"{provider.Provider.Value} API key");
            Button saveCredential = new()
            {
                Content = configuration.CredentialState is ModelProviderCredentialState.Configured
                    ? "Replace API key"
                    : "Save API key",
                IsEnabled = false,
            };
            saveCredential.Classes.Add("command");
            AutomationProperties.SetName(saveCredential, $"Save {provider.Provider.Value} API key");
            credential.GetObservable(TextBox.TextProperty).Subscribe(value =>
                saveCredential.IsEnabled = !settingsState.IsBusy && !string.IsNullOrWhiteSpace(value));
            saveCredential.Click += async (_, _) =>
            {
                await store.SetModelProviderCredentialAsync(new(
                    configuration.Provider,
                    new(credential.Text ?? string.Empty)), cancellationToken);
                credential.Text = string.Empty;
            };
            Grid credentialRow = new()
            {
                ColumnDefinitions = new("*,Auto"),
                ColumnSpacing = 10,
                Children = { credential },
            };
            Grid.SetColumn(saveCredential, 1);
            credentialRow.Children.Add(saveCredential);
            content.Children.Add(new TextBlock
            {
                Text = $"Credential: {configuration.CredentialState}" +
                       (configuration.CredentialMessage is null
                           ? string.Empty
                           : $" · {configuration.CredentialMessage}"),
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(credentialRow);
            content.Children.Add(new TextBlock
            {
                Text = "The key is write-only, stored in Linux Secret Service, and never saved in XML or application state. An environment value takes precedence when configured.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        content.Children.Add(save);
        content.Children.Add(new TextBlock
        {
            Text = configuration.RequiresRestart
                ? "Restart required: saved configuration differs from this running process."
                : "Endpoint, model, dimension, Ollama context, timeout, and secret-reference changes apply after restart.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
        });
        return new Border { Classes = { "card", "row" }, Child = content };
    }

    private static TextBox ProviderTextBox(string value, string accessibleName)
    {
        TextBox field = new() { Text = value, MinWidth = 220 };
        AutomationProperties.SetName(field, accessibleName);
        return field;
    }

    private static TextBox Multiline(string value, string accessibleName, double height)
    {
        TextBox field = new()
        {
            Text = value,
            AcceptsReturn = true,
            Height = height,
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(field, accessibleName);
        return field;
    }

    private static IReadOnlyList<string> Lines(string? value) =>
        (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

    private NumericUpDown ProviderNumber(
        int value,
        int minimum,
        int maximum,
        string accessibleName)
    {
        NumericUpDown field = new()
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(field, accessibleName);
        return field;
    }

    private static void AddProviderField(
        Grid grid,
        int row,
        int column,
        string label,
        Control field)
    {
        StackPanel container = new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                field,
            },
        };
        Grid.SetRow(container, row);
        Grid.SetColumn(container, column);
        grid.Children.Add(container);
    }

}
