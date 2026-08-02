using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Avalonia;

internal sealed class GoalSettingsDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private GoalView goal;
    private readonly NumericUpDown reviewCycles = new()
    {
        Minimum = 1,
        Maximum = 20,
        Increment = 1,
        FormatString = "0",
    };
    private readonly CheckBox enableRemote = new()
    {
        Content = "Authorize a goal-wide remote spending cap",
    };
    private readonly TextBox remoteUsd = new() { PlaceholderText = "USD, for example 2.00" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button save = new() { Content = "Save private limits" };
    private readonly Button routes = new() { Content = "Role routes and models…" };

    internal GoalSettingsDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Goal settings";
        Width = 620;
        Height = 510;
        MinWidth = 520;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        LoadGoal();
        WireInteractions();
    }

    private Control BuildContent()
    {
        AutomationProperties.SetName(reviewCycles, "Maximum review cycles");
        AutomationProperties.SetName(enableRemote, "Authorize remote spending cap");
        AutomationProperties.SetName(remoteUsd, "Remote spending cap in US dollars");
        AutomationProperties.SetName(save, "Save goal settings");
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        StackPanel limits = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Execution limits", FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Maximum review cycles (1–20)",
                    Classes = { "muted" },
                },
                reviewCycles,
                enableRemote,
                remoteUsd,
                new TextBlock
                {
                    Text = "Enabling this cap explicitly authorizes aggregate remote model spend for this goal up to the exact amount. Disabling it keeps the goal local-only.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                },
            },
        };
        Border limitsCard = new()
        {
            Classes = { "settings-card" },
            Child = limits,
        };
        StackPanel roles = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Progressive overrides", FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Ordinary role defaults come from Settings. Open this only when the current goal needs a different local or remote route; per-run output ceilings remain explicit when a run starts.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                },
                routes,
            },
        };
        Border rolesCard = new()
        {
            Classes = { "settings-card" },
            Child = roles,
        };
        Grid actions = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        actions.Children.Add(status);
        Grid.SetColumn(save, 1);
        save.Classes.Add("primary");
        actions.Children.Add(save);
        Grid.SetColumn(close, 2);
        actions.Children.Add(close);
        return new Grid
        {
            RowDefinitions = new("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 12,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = goal.Title,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                AtRow(new TextBlock
                {
                    Text = "Private draft settings. These values cannot be changed after planning starts.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                }, 1),
                AtRow(limitsCard, 2),
                AtRow(rolesCard, 3),
                AtRow(actions, 4),
            },
        };
    }

    private void LoadGoal()
    {
        reviewCycles.Value = goal.ReviewCycleLimit.Value;
        enableRemote.IsChecked = goal.RemoteBudget is not null;
        remoteUsd.Text = goal.RemoteBudget is null
            ? string.Empty
            : GoalPresentationFormatter.ToUsd(goal.RemoteBudget.Value);
        remoteUsd.IsEnabled = enableRemote.IsChecked is true;
    }

    private void WireInteractions()
    {
        enableRemote.IsCheckedChanged += (_, _) =>
            remoteUsd.IsEnabled = enableRemote.IsChecked is true;
        save.Click += async (_, _) => await SaveAsync();
        routes.Click += async (_, _) =>
        {
            await store.DiscoverGoalModelsAsync(goal.Id, cancellationToken);
            if (store.Current.Goals.ModelCatalog is not null)
            {
                await new ModelRoutingDialog(store, goal, cancellationToken).ShowDialog(this);
            }
        };
    }

    private async Task SaveAsync()
    {
        int cycles = decimal.ToInt32(reviewCycles.Value ?? goal.ReviewCycleLimit.Value);
        MicroUsdAmount? budget = null;
        if (enableRemote.IsChecked is true)
        {
            if (!decimal.TryParse(remoteUsd.Text, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal usd) || usd <= 0 ||
                usd > long.MaxValue / 1_000_000m)
            {
                status.Text = "Enter a positive USD cap using a decimal point.";
                return;
            }

            budget = new(decimal.ToInt64(decimal.Round(
                usd * 1_000_000m,
                0,
                MidpointRounding.AwayFromZero)));
        }

        await store.UpdateGoalSettingsAsync(new(
            goal.Id,
            new ReviewCycleLimit(cycles),
            budget,
            goal.UpdatedAt), cancellationToken);
        if (store.Current.Goals.SelectedGoal is { } updated && updated.UpdatedAt != goal.UpdatedAt)
        {
            goal = updated;
            status.Text = store.Current.Goals.Status ?? "Saved.";
            LoadGoal();
        }
        else
        {
            status.Text = store.Current.Goals.Status ?? "Settings were not saved.";
        }
    }

    private static T AtRow<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}

internal sealed class BudgetExtensionDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly TextBox remoteUsd = new() { PlaceholderText = "New total cap in USD" };
    private readonly TextBox reason = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        MaxLength = 2_000,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };

    internal BudgetExtensionDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Increase remote cap";
        Width = 570;
        Height = 430;
        MinWidth = 500;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        AutomationProperties.SetName(remoteUsd, "New total remote spending cap in US dollars");
        AutomationProperties.SetName(reason, "Required budget extension reason");
        Button approve = new() { Content = "Increase cap", Classes = { "primary" } };
        AutomationProperties.SetName(approve, "Approve remote budget increase");
        approve.Click += async (_, _) => await ApproveAsync();
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Grid actions = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        actions.Children.Add(status);
        Grid.SetColumn(approve, 1);
        actions.Children.Add(approve);
        Grid.SetColumn(cancel, 2);
        actions.Children.Add(cancel);
        return new Grid
        {
            RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "Increase remote spending authority",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                AtRow(new TextBlock
                {
                    Text = $"Current cap: " + (goal.RemoteBudget is null
                        ? "$0 (local-only)"
                        : $"${GoalPresentationFormatter.ToUsd(goal.RemoteBudget.Value)}"),
                    Classes = { "muted" },
                }, 1),
                AtRow(new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "New total cap (USD)" },
                        remoteUsd,
                    },
                }, 2),
                AtRow(new TextBlock
                {
                    Text = "This increase is durable and auditable. It does not retry a failed " +
                           "model call; use the retry action separately after reviewing cost evidence.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                }, 3),
                AtRow(new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "Required reason" },
                        reason,
                    },
                }, 4),
                AtRow(actions, 5),
            },
        };
    }

    private async Task ApproveAsync()
    {
        if (!decimal.TryParse(remoteUsd.Text, NumberStyles.Number,
                CultureInfo.InvariantCulture, out decimal usd) || usd <= 0 ||
            usd > long.MaxValue / 1_000_000m)
        {
            status.Text = "Enter a positive USD cap using a decimal point.";
            return;
        }

        MicroUsdAmount newBudget = new(decimal.ToInt64(decimal.Round(
            usd * 1_000_000m,
            0,
            MidpointRounding.AwayFromZero)));
        if (newBudget.Value <= (goal.RemoteBudget?.Value ?? 0))
        {
            status.Text = "The new total cap must be larger than the current cap.";
            return;
        }

        string extensionReason = reason.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(extensionReason))
        {
            status.Text = "Enter why this additional remote spend is needed.";
            return;
        }

        await store.ExtendGoalBudgetAsync(new(
            goal.Id,
            goal.RemoteBudget,
            newBudget,
            new(extensionReason)), cancellationToken);
        if (store.Current.Goals.SelectedGoal?.RemoteBudget == newBudget)
        {
            Close();
        }
        else
        {
            status.Text = store.Current.Goals.Status ?? "The cap was not increased.";
        }
    }

    private static T AtRow<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
