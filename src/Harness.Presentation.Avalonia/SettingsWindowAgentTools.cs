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
    private Control AgentToolsPage()
    {
        StackPanel modules = new() { Spacing = 12 };
        Dictionary<string, CheckBox> exposure = new(StringComparer.Ordinal);
        HashSet<string> direct = settingsState.AgentToolExposure?.DirectModules
            .Select(item => item.Value).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (AgentToolModule module in AgentToolCatalog.Default.Modules)
        {
            string roles = string.Join(", ", module.Roles.Select(role => role.ToString()));
            string operations = module.Operations.Count == 0
                ? "No model schemas exposed"
                : string.Join(", ", module.Operations.Select(operation => operation.Value));
            string status = module.Availability is AgentToolModuleAvailability.Available
                ? "Available"
                : $"Planned · {module.UnavailableReason}";
            StackPanel card = new()
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = module.DisplayName,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = module.Summary,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = $"{status}\nSource: {module.Source.Value} · Roles: {roles}\n" +
                               $"Exposure: {module.Exposure} · Authority: {module.Authority}\n" +
                               $"Mode: {(module.IsOptional ? "Optional" : "Required core")}\n" +
                               $"Operations: {operations}",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
            if (module.IsOptional && module.Availability is AgentToolModuleAvailability.Available &&
                module.Exposure is AgentToolExposure.OnDemand)
            {
                CheckBox expose = new()
                {
                    Content = "Expose directly on every eligible role turn",
                    IsChecked = direct.Contains(module.Id.Value),
                };
                AutomationProperties.SetName(expose, $"Expose {module.DisplayName} directly");
                exposure[module.Id.Value] = expose;
                card.Children.Add(expose);
            }
            modules.Children.Add(new Border
            {
                Classes = { "card", "row" },
                Child = card,
            });
        }

        Button saveExposure = new() { Content = "Save optional exposure defaults" };
        saveExposure.Classes.Add("primary");
        saveExposure.Click += async (_, _) => await store.SaveAgentToolExposureAsync(
            exposure.Where(item => item.Value.IsChecked == true)
                .Select(item => new AgentToolModuleId(item.Key)).ToArray(), cancellationToken);
        modules.Children.Add(saveExposure);

        int externalConnections = settingsState.McpSettings?.Connections.Count ?? 0;
        modules.Children.Add(new Border
        {
            Classes = { "card" },
            Child = new TextBlock
            {
                Text = $"External MCP sources: {externalConnections} configured. " +
                       "Connection health and read-only eligibility are managed under MCP connections. " +
                       "External tools do not inherit built-in authority.",
                TextWrapping = TextWrapping.Wrap,
            },
        });

        return Page(
            "Agent tools",
            "See what models can use, where each capability comes from, and which authority boundary applies. On-demand requests grant schemas for one next role turn; saved direct exposure never bypasses operation approval.",
            modules);
    }

}
