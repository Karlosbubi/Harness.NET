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
    private Control ModelsAndRolesPage()
    {
        AgentDefaultsSnapshot? snapshot = settingsState.AgentDefaults;
        Button discover = new()
        {
            Content = snapshot?.Models.Count > 0 ? "Refresh available models" : "Discover available models",
            IsEnabled = !settingsState.IsBusy,
        };
        discover.Classes.Add("command");
        AutomationProperties.SetName(discover, "Discover available agent models");
        discover.Click += async (_, _) => await store.DiscoverAgentDefaultsAsync(cancellationToken);

        StackPanel roles = new() { Spacing = 12 };
        if (snapshot is null)
        {
            roles.Children.Add(new TextBlock
            {
                Text = "Loading agent defaults…",
                Classes = { "muted" },
            });
        }
        else
        {
            foreach (AgentRoleDefault roleDefault in snapshot.Roles.OrderBy(item => item.Role))
            {
                roles.Children.Add(AgentRoleDefaultCard.Create(
                    roleDefault,
                    snapshot.Models,
                    snapshot.DefaultIssues.FirstOrDefault(issue => issue.Role == roleDefault.Role),
                    settingsState.IsBusy,
                    (role, candidate, reasoningPolicy) => store.UpdateAgentDefaultAsync(
                        role, candidate, reasoningPolicy, cancellationToken)));
            }
        }

        string issues = snapshot?.Issues.Count > 0
            ? string.Join("\n", snapshot.Issues.Select(issue =>
                $"{issue.Provider.Value}: {issue.Message}"))
            : string.Empty;
        return Page(
            "Models & roles",
            "Choose ordinary routing and output defaults. A goal can disclose an override when needed.",
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
                            Text = "Remote role defaults can run immediately for goals using the Unlimited or Capped spend mode. Use Privacy & limits to opt into a hard cap or local-only default.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    discover,
                    roles,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? issues,
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                },
            });
    }

    private Control PrivacyAndLimitsPage()
    {
        RemoteSpendPreference current = settingsState.RemoteSpendPreference;
        RemoteSpendChoice[] choices =
        [
            new(RemoteSpendMode.Unlimited, "Unlimited remote spend (default)"),
            new(RemoteSpendMode.Capped, "Set an aggregate spending cap"),
            new(RemoteSpendMode.LocalOnly, "Local models only"),
        ];
        ComboBox mode = new()
        {
            ItemsSource = choices,
            SelectedItem = choices.First(choice => choice.Mode == current.Mode),
            MinWidth = 320,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(mode, "Default remote spending mode");
        TextBox cap = new()
        {
            Text = current.Cap is null
                ? string.Empty
                : GoalPresentationFormatter.ToUsd(current.Cap.Value),
            PlaceholderText = "USD, for example 10.00",
            MinWidth = 240,
            IsEnabled = current.Mode is RemoteSpendMode.Capped && !settingsState.IsBusy,
        };
        AutomationProperties.SetName(cap, "Default remote spending cap in US dollars");
        TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };
        mode.SelectionChanged += (_, _) => cap.IsEnabled =
            mode.SelectedItem is RemoteSpendChoice { Mode: RemoteSpendMode.Capped } &&
            !settingsState.IsBusy;
        Button save = new()
        {
            Content = "Save cost-control default",
            IsEnabled = !settingsState.IsBusy,
        };
        save.Classes.Add("primary");
        AutomationProperties.SetName(save, "Save default remote spending policy");
        save.Click += async (_, _) =>
        {
            if (mode.SelectedItem is not RemoteSpendChoice selected)
            {
                validation.Text = "Choose a remote-spending mode.";
                return;
            }

            MicroUsdAmount? amount = null;
            if (selected.Mode is RemoteSpendMode.Capped)
            {
                if (!TryParseUsd(cap.Text, out amount))
                {
                    validation.Text = "Enter a positive USD cap using a decimal point and at most six decimal places.";
                    return;
                }
            }

            await store.UpdateRemoteSpendPreferenceAsync(
                new(selected.Mode, amount), cancellationToken);
        };

        return Page(
            "Privacy & limits",
            "Choose the spend policy preselected for newly created goals. Every goal creation surface shows the choice again.",
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
                            Text = "Unlimited remote spend is the convenience default. It removes Harness.NET's aggregate dollar ceiling; provider billing and account limits still apply. Opt into a cap or local-only execution here when you want hard cost control.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    new TextBlock { Text = "Default for new goals", FontWeight = FontWeight.SemiBold },
                    mode,
                    new TextBlock { Text = "Aggregate cap (USD)", FontWeight = FontWeight.SemiBold },
                    cap,
                    validation,
                    save,
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
    }

}
