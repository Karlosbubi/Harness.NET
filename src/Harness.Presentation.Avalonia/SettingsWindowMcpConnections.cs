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
    private Control McpConnectionsPage()
    {
        Button refresh = new()
        {
            Content = "Refresh active connections",
            IsEnabled = !settingsState.IsBusy,
        };
        refresh.Classes.Add("command");
        AutomationProperties.SetName(refresh, "Refresh stateless MCP connections");
        refresh.Click += async (_, _) => await store.RefreshMcpAsync(cancellationToken);

        TextBox name = ProviderTextBox(string.Empty, "New MCP connection name");
        name.PlaceholderText = "docs";
        TextBox endpoint = ProviderTextBox(string.Empty, "New MCP endpoint");
        endpoint.PlaceholderText = "https://example.test/mcp";
        NumericUpDown timeout = ProviderNumber(30, 1, 3_600, "New MCP request timeout seconds");
        ComboBox kind = new()
        {
            ItemsSource = Enum.GetValues<McpConnectionKind>(),
            SelectedItem = McpConnectionKind.ReadOnly,
            MinWidth = 220,
        };
        AutomationProperties.SetName(kind, "New MCP connection kind");
        TextBox clientId = ProviderTextBox("harness-controller", "New Harness control client ID");
        TextBox allowedTools = new()
        {
            Text = string.Empty,
            AcceptsReturn = true,
            Height = 100,
            PlaceholderText = "One exact harness_ tool ID per line",
        };
        AutomationProperties.SetName(allowedTools, "New Harness control allowed tool IDs");
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is McpConnectionKind.HarnessControl &&
                string.IsNullOrWhiteSpace(allowedTools.Text))
            {
                allowedTools.Text = string.Join(
                    Environment.NewLine, DefaultHarnessControlTools);
            }
        };
        CheckBox enabled = new() { Content = "Enable after restart", IsChecked = true };
        AutomationProperties.SetName(enabled, "Enable new MCP connection after restart");
        Button add = new() { Content = "Add connection", IsEnabled = !settingsState.IsBusy };
        add.Classes.Add("accent");
        AutomationProperties.SetName(add, "Add stateless MCP connection");
        add.Click += async (_, _) =>
        {
            await store.SaveMcpConnectionAsync(new(
                new(name.Text ?? string.Empty),
                new(endpoint.Text ?? string.Empty),
                new(decimal.ToInt32(timeout.Value ?? 30)),
                (McpConnectionKind)(kind.SelectedItem ?? McpConnectionKind.ReadOnly),
                kind.SelectedItem is McpConnectionKind.HarnessControl
                    ? new(clientId.Text ?? string.Empty)
                    : null,
                kind.SelectedItem is McpConnectionKind.HarnessControl
                    ? Lines(allowedTools.Text).Select(tool =>
                        new McpAllowedToolName(tool)).ToArray()
                    : [],
                enabled.IsChecked == true), cancellationToken);
            if (store.Current.Settings.Status?.StartsWith(
                    "MCP connection saved", StringComparison.Ordinal) == true)
            {
                name.Text = string.Empty;
                endpoint.Text = string.Empty;
                allowedTools.Text = string.Empty;
            }
        };

        Grid newFields = new()
        {
            ColumnDefinitions = new("*,*"),
            RowDefinitions = new("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        AddProviderField(newFields, 0, 0, "Connection name", name);
        AddProviderField(newFields, 0, 1, "Streamable HTTP endpoint", endpoint);
        AddProviderField(newFields, 1, 0, "Request timeout (seconds)", timeout);
        AddProviderField(newFields, 1, 1, "Connection kind", kind);
        AddProviderField(newFields, 2, 0, "Harness control client ID", clientId);
        AddProviderField(newFields, 2, 1, "Harness control allowed tools", allowedTools);
        Grid.SetRow(enabled, 3);
        Grid.SetColumn(enabled, 1);
        newFields.Children.Add(enabled);
        Grid.SetRow(add, 4);
        newFields.Children.Add(add);

        StackPanel connections = new() { Spacing = 12 };
        foreach (McpConnectionSettingsView connection in
                 settingsState.McpSettings?.Connections ?? [])
        {
            connections.Children.Add(McpConnectionCard(connection));
        }

        if (connections.Children.Count == 0)
        {
            connections.Children.Add(new TextBlock
            {
                Text = "No MCP connections are configured. Add a stateless Streamable HTTP endpoint above.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return Page(
            "MCP connections",
            "Manage first-class Model Context Protocol endpoints. Ordinary connections expose only explicitly read-only, non-destructive tools. Harness control is a separate loopback-only, exactly allowlisted Lead delegation mode.",
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
                            Text = "Read-only connections fail closed on missing or unsafe annotations. Harness control additionally requires a loopback Harness.NET server identity, stable client ID, and exact harness_ tool allowlist. The local server is unauthenticated and the client ID is only attribution. Control tools are exposed only to Lead. Saved changes require restart.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    new Border
                    {
                        Classes = { "card", "row" },
                        Child = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "Add stateless connection",
                                    FontSize = 16,
                                    FontWeight = FontWeight.SemiBold,
                                },
                                newFields,
                            },
                        },
                    },
                    refresh,
                    connections,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
    }

    private Control McpConnectionCard(McpConnectionSettingsView connection)
    {
        TextBox endpoint = ProviderTextBox(connection.Endpoint.Value,
            $"{connection.Name.Value} MCP endpoint");
        NumericUpDown timeout = ProviderNumber(
            connection.RequestTimeout.Value, 1, 3_600,
            $"{connection.Name.Value} MCP request timeout seconds");
        ComboBox kind = new()
        {
            ItemsSource = Enum.GetValues<McpConnectionKind>(),
            SelectedItem = connection.Kind,
            MinWidth = 220,
        };
        AutomationProperties.SetName(kind, $"{connection.Name.Value} MCP connection kind");
        TextBox clientId = ProviderTextBox(connection.ClientId?.Value ?? string.Empty,
            $"{connection.Name.Value} Harness control client ID");
        TextBox allowedTools = new()
        {
            Text = string.Join(Environment.NewLine,
                connection.AllowedTools.Select(tool => tool.Value)),
            AcceptsReturn = true,
            Height = 100,
        };
        AutomationProperties.SetName(allowedTools,
            $"{connection.Name.Value} Harness control allowed tool IDs");
        CheckBox enabled = new()
        {
            Content = "Enabled",
            IsChecked = connection.IsEnabled,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(enabled, $"Enable {connection.Name.Value} MCP connection");
        Button save = new() { Content = "Save", IsEnabled = !settingsState.IsBusy };
        save.Classes.Add("command");
        save.Click += async (_, _) => await store.SaveMcpConnectionAsync(new(
            connection.Name,
            new(endpoint.Text ?? string.Empty),
            new(decimal.ToInt32(timeout.Value ?? 0)),
            (McpConnectionKind)(kind.SelectedItem ?? McpConnectionKind.ReadOnly),
            kind.SelectedItem is McpConnectionKind.HarnessControl
                ? new(clientId.Text ?? string.Empty)
                : null,
            kind.SelectedItem is McpConnectionKind.HarnessControl
                ? Lines(allowedTools.Text).Select(tool =>
                    new McpAllowedToolName(tool)).ToArray()
                : [],
            enabled.IsChecked == true), cancellationToken);
        Button remove = new() { Content = "Remove", IsEnabled = !settingsState.IsBusy };
        remove.Classes.Add("danger");
        AutomationProperties.SetName(remove, $"Remove {connection.Name.Value} MCP connection");
        remove.Click += async (_, _) => await store.DeleteMcpConnectionAsync(
            connection.Name, cancellationToken);

        Grid fields = new()
        {
            ColumnDefinitions = new("*,Auto"),
            RowDefinitions = new("Auto,Auto,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10,
            Children = { endpoint },
        };
        Grid.SetColumn(timeout, 1);
        fields.Children.Add(timeout);
        Grid.SetRow(kind, 1);
        fields.Children.Add(kind);
        Grid.SetRow(clientId, 1);
        Grid.SetColumn(clientId, 1);
        fields.Children.Add(clientId);
        Grid.SetRow(allowedTools, 2);
        Grid.SetColumnSpan(allowedTools, 2);
        fields.Children.Add(allowedTools);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { enabled, save, remove },
        };
        string protocol = connection.NegotiatedProtocolVersion is null
            ? "No protocol negotiated"
            : $"MCP {connection.NegotiatedProtocolVersion}";
        return new Border
        {
            Classes = { "card", "row" },
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = connection.Name.Value,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"{connection.State} · {connection.Kind} · {protocol} · {connection.DiscoveredTools} tool(s), {connection.AgentEligibleTools} eligible, {connection.RejectedTools} rejected",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    fields,
                    actions,
                    new TextBlock
                    {
                        Text = connection.Message ?? (connection.RequiresRestart
                            ? "Restart required before this saved configuration becomes active."
                            : "Connection discovery is ready."),
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
    }

}
