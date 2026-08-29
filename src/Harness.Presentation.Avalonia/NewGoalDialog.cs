using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class NewGoalDialog : Window
{
    private readonly string workspaceId;
    private readonly TextBox title = new();
    private readonly TextBox objective = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 140,
    };
    private readonly TextBox reviewLimit = new() { Text = "3" };
    private readonly ComboBox remoteMode = new();
    private readonly TextBox remoteBudget = new() { PlaceholderText = "USD, for example 10.00" };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal NewGoalDialog(
        string workspaceId,
        RemoteSpendPreference? remoteSpendPreference = null)
    {
        this.workspaceId = workspaceId;
        RemoteSpendPreference preference = remoteSpendPreference ?? RemoteSpendPreference.Default;
        RemoteSpendChoice[] choices =
        [
            new(RemoteSpendMode.Unlimited, "Unlimited remote spend (default)"),
            new(RemoteSpendMode.Capped, "Set an aggregate spending cap"),
            new(RemoteSpendMode.LocalOnly, "Local models only"),
        ];
        remoteMode.ItemsSource = choices;
        remoteMode.SelectedItem = choices.First(choice => choice.Mode == preference.Mode);
        remoteBudget.Text = preference.Cap is null
            ? string.Empty
            : GoalPresentationFormatter.ToUsd(preference.Cap.Value);
        remoteBudget.IsEnabled = preference.Mode is RemoteSpendMode.Capped;
        Title = "New goal";
        Width = 680;
        Height = 560;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
    }

    internal GoalCreateRequest? Result { get; private set; }

    private Control BuildContent()
    {
        AutomationProperties.SetName(title, "Goal title");
        AutomationProperties.SetName(objective, "Goal objective");
        AutomationProperties.SetName(reviewLimit, "Review-cycle limit");
        AutomationProperties.SetName(remoteMode, "Remote spending mode");
        AutomationProperties.SetName(remoteBudget, "Remote budget in USD");
        AutomationProperties.SetName(validation, "New goal validation");
        StackPanel panel = new() { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Title" });
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "Objective" });
        panel.Children.Add(objective);
        panel.Children.Add(new TextBlock { Text = "Review-cycle limit (1–20)" });
        panel.Children.Add(reviewLimit);
        panel.Children.Add(new Border
        {
            Classes = { "card", "attention" },
            Child = new TextBlock
            {
                Text = "Unlimited remote spend is selected for convenience. Provider charges still apply. Opt into a hard cap or local-only execution for this goal below.",
                TextWrapping = TextWrapping.Wrap,
            },
        });
        panel.Children.Add(new TextBlock { Text = "Remote spending" });
        panel.Children.Add(remoteMode);
        panel.Children.Add(new TextBlock { Text = "Aggregate cap USD" });
        panel.Children.Add(remoteBudget);
        panel.Children.Add(validation);

        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button save = new() { Content = "Create goal" };
        save.Click += (_, _) => Save();
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save },
        });
        remoteMode.SelectionChanged += (_, _) => remoteBudget.IsEnabled =
            remoteMode.SelectedItem is RemoteSpendChoice { Mode: RemoteSpendMode.Capped };
        return panel;
    }

    private void Save()
    {
        if (!int.TryParse(reviewLimit.Text, NumberStyles.None, CultureInfo.InvariantCulture,
                out int cycles) || cycles is < 1 or > 20)
        {
            validation.Text = "Review-cycle limit must be an integer from 1 through 20.";
            return;
        }

        if (remoteMode.SelectedItem is not RemoteSpendChoice spendChoice)
        {
            validation.Text = "Choose a remote-spending mode.";
            return;
        }

        MicroUsdAmount? cap = null;
        if (spendChoice.Mode is RemoteSpendMode.Capped &&
            (!TryParseBudget(remoteBudget.Text, out cap, out string? error) || cap is null))
        {
            validation.Text = error ?? "Enter a positive USD cap.";
            return;
        }

        MicroUsdAmount? budget = new RemoteSpendPreference(
            spendChoice.Mode,
            cap).ToGoalBudget();

        Result = new(
            workspaceId,
            title.Text ?? string.Empty,
            objective.Text ?? string.Empty,
            new(cycles),
            budget);
        Close();
    }

    private sealed record RemoteSpendChoice(RemoteSpendMode Mode, string Name)
    {
        public override string ToString() => Name;
    }

    private static bool TryParseBudget(
        string? value,
        out MicroUsdAmount? budget,
        out string? error)
    {
        string input = value?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            budget = null;
            error = null;
            return true;
        }

        if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture,
                out decimal usd) || usd <= 0)
        {
            budget = null;
            error = "Remote budget must be a positive USD amount using '.' as the decimal separator.";
            return false;
        }

        decimal microUsd = usd * 1_000_000m;
        if (microUsd != decimal.Truncate(microUsd) || microUsd > long.MaxValue)
        {
            budget = null;
            error = "Remote budget supports at most six decimal places and must fit the supported range.";
            return false;
        }

        budget = new((long)microUsd);
        error = null;
        return true;
    }
}

