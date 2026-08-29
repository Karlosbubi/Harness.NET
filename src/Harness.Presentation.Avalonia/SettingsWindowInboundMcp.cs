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
    private Control InboundMcpPage()
    {
        InboundMcpSettingsView? snapshot = settingsState.InboundMcpSettings;
        InboundControlSettings configured = snapshot?.Settings ?? new(
            false, InboundControlMode.Normal, new Uri("http://127.0.0.1:57431/mcp"), [],
            [new("harness_application"), new("harness_workspace"), new("harness_tree"),
                new("harness_read_range"), new("harness_git"), new("harness_git_history"),
                new("harness_git_commit"), new("harness_git_blame"), new("harness_project_graph"),
                new("harness_goals"), new("harness_evidence"),
                new("harness_workflow_evidence"), new("harness_goal_models"),
                new("harness_commit_preview"), new("harness_ui"),
                new("harness_open_document"), new("harness_audit"),
                new("harness_evaluation_snapshot"),
                new("harness_code_problems"), new("harness_code_symbol"),
                new("harness_code_definition"), new("harness_code_references"),
                new("harness_code_implementations"), new("harness_code_inspection"),
                new("harness_code_actions")],
            [], TimeSpan.FromSeconds(30), 500, 1_000, false);

        CheckBox enabled = new()
        {
            Content = "Enable local loopback MCP server",
            IsChecked = configured.IsEnabled
        };
        AutomationProperties.SetName(enabled, "Enable inbound MCP server");
        ComboBox mode = new()
        {
            ItemsSource = Enum.GetValues<InboundControlMode>(),
            SelectedItem = configured.Mode,
            MinWidth = 260,
        };
        AutomationProperties.SetName(mode, "Inbound MCP mode");
        TextBox endpoint = new() { Text = configured.Endpoint.AbsoluteUri };
        AutomationProperties.SetName(endpoint, "Inbound MCP loopback endpoint");
        TextBox clients = new()
        {
            Text = string.Join(Environment.NewLine, configured.AllowedClients.Select(item => item.Value)),
            AcceptsReturn = true,
            Height = 70,
            PlaceholderText = "One allowed client ID per line; empty allows anonymous local clients",
        };
        AutomationProperties.SetName(clients, "Allowed inbound MCP client IDs");
        TextBox tools = new()
        {
            Text = string.Join(Environment.NewLine, configured.AllowedTools.Select(item => item.Value)),
            AcceptsReturn = true,
            Height = 110,
        };
        AutomationProperties.SetName(tools, "Allowed inbound MCP tool IDs");
        TextBox approvals = new()
        {
            Text = string.Join(Environment.NewLine,
                configured.ApprovalRequiredTools.Select(item => item.Value)),
            AcceptsReturn = true,
            Height = 70,
            PlaceholderText = "One allowed tool ID per line",
        };
        AutomationProperties.SetName(approvals,
            "Inbound MCP tool IDs requiring explicit approval");
        NumericUpDown timeout = new()
        {
            Minimum = 1,
            Maximum = 300,
            Value = (decimal)configured.RequestTimeout.TotalSeconds
        };
        AutomationProperties.SetName(timeout, "Inbound MCP request timeout in seconds");
        NumericUpDown resultLimit = new()
        {
            Minimum = 1,
            Maximum = 5000,
            Value = configured.ResultLimit
        };
        AutomationProperties.SetName(resultLimit, "Inbound MCP result limit");
        NumericUpDown retention = new()
        {
            Minimum = 0,
            Maximum = 100000,
            Value = configured.AuditRetention
        };
        AutomationProperties.SetName(retention, "Inbound MCP audit retention");
        string policySummary = snapshot?.ToolPolicies.Count > 0
            ? string.Join(Environment.NewLine, snapshot.ToolPolicies.Select(policy =>
                $"{policy.Id.Value} · " +
                (policy.IsReadOnly ? "read" : "mutation") +
                (policy.IsExecution ? " · execution" : string.Empty) +
                (policy.IsSensitive ? " · sensitive" : string.Empty) +
                (policy.IsDestructive ? " · destructive" : string.Empty) +
                (policy.IsIdempotent ? " · idempotent" : string.Empty)))
            : "Tool policy catalog unavailable.";

        Button save = new() { Content = "Apply server settings" };
        save.Classes.Add("primary");
        save.IsEnabled = !settingsState.IsBusy;
        save.Click += async (_, _) =>
        {
            if (!Uri.TryCreate(endpoint.Text?.Trim(), UriKind.Absolute, out Uri? parsed)) return;
            await store.SaveInboundMcpAsync(configured with
            {
                IsEnabled = enabled.IsChecked == true,
                Mode = mode.SelectedItem is InboundControlMode selected ? selected : InboundControlMode.Normal,
                Endpoint = parsed,
                AllowedClients = Lines(clients.Text).Select(value => new InboundControlClientId(value)).ToArray(),
                AllowedTools = Lines(tools.Text).Select(value => new InboundControlToolId(value)).ToArray(),
                ApprovalRequiredTools = Lines(approvals.Text)
                    .Select(value => new InboundControlToolId(value)).ToArray(),
                RequestTimeout = TimeSpan.FromSeconds((double)(timeout.Value ?? 30)),
                ResultLimit = (int)(resultLimit.Value ?? 500),
                AuditRetention = (int)(retention.Value ?? 1000),
            }, cancellationToken);
        };
        Button resetEvaluation = new()
        {
            Content = "Reset isolated fixture",
            IsEnabled = configured.Mode is InboundControlMode.IsolatedEvaluation &&
                !settingsState.IsBusy,
        };
        resetEvaluation.Click += async (_, _) =>
            await store.ResetInboundMcpEvaluationAsync(cancellationToken);

        InboundControlStatus? status = snapshot?.Status;
        StackPanel activeClients = new() { Spacing = 6 };
        foreach (InboundControlClientStatus client in status?.ActiveClients ?? [])
        {
            Button disconnect = new() { Content = $"Disconnect {client.Id.Value}" };
            disconnect.Click += async (_, _) => await store.DisconnectInboundMcpClientAsync(
                client.Id, cancellationToken);
            activeClients.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"{client.Id.Value} · {client.RequestCount} request(s) · {client.LastSeenAt:O}",
                        VerticalAlignment = VerticalAlignment.Center },
                    disconnect,
                },
            });
        }
        if (activeClients.Children.Count == 0)
            activeClients.Children.Add(new TextBlock { Text = "No active clients.", Classes = { "muted" } });

        return Page(
            "Harness control",
            "Expose typed Harness.NET inspection to local MCP clients. The server is unauthenticated and cannot bind beyond loopback; workspace trust, baselines, approvals, capture consent, and execution policy still apply.",
            new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = status is null ? "Runtime status unavailable." :
                            $"{(status.IsRunning ? "ACTIVE" : "INACTIVE")} · {status.Mode} · instance {status.InstanceId}\n" +
                            $"{status.Endpoint} · local loopback · no authentication" +
                            (status.Error is null ? string.Empty : $"\n{status.Error}"),
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { status?.IsRunning == true ? "status-success" : "muted" },
                    },
                    enabled,
                    Labeled("Mode", mode, "Evaluation mode requires a dedicated temporary root supplied at process startup."),
                    Labeled("Loopback endpoint", endpoint, "Plain HTTP is accepted only on 127.0.0.1, ::1, or localhost."),
                    Labeled("Allowed clients", clients, "Empty accepts clients without a header as local-anonymous. Otherwise X-Harness-Client must match exactly. This identifier is not authentication."),
                    Labeled("Allowed tools", tools, "Closed typed tool IDs only. Unknown IDs expose nothing."),
                    Labeled("Require explicit approval", approvals,
                        "These tools stay out of discovery until the developer removes the approval requirement and applies settings."),
                    new Border { Classes = { "card" }, Child = new TextBlock
                    {
                        Text = "Closed tool catalog\n" + policySummary,
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    } },
                    Labeled("Request timeout (seconds)", timeout),
                    Labeled("Maximum results", resultLimit),
                    Labeled("Audit records retained", retention),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { save, resetEvaluation } },
                    new TextBlock { Text = "Active clients", FontWeight = FontWeight.SemiBold },
                    activeClients,
                    new TextBlock
                    {
                        Text = "Never exposed: generic shell, SQL, click/type, coordinates, desktop control, silent screenshots, secrets, or arbitrary command dispatch.",
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                },
            });
    }

}
