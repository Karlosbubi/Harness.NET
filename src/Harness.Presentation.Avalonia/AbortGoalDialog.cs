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

internal sealed class AbortGoalDialog : Window
{
    private readonly TextBox reason = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        PlaceholderText = "Optional note for the audit trail",
    };

    internal AbortGoalDialog(GoalView goal)
    {
        Title = "Abort goal";
        Width = 620;
        Height = 390;
        MinWidth = 520;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(reason, "Goal abort reason");
        Button cancel = new() { Content = "Keep goal" };
        cancel.Click += (_, _) => Close();
        Button abort = new() { Content = "Abort & start new goal" };
        abort.Classes.Add("danger");
        AutomationProperties.SetName(abort, $"Confirm abort goal {goal.Title}");
        abort.Click += (_, _) =>
        {
            string value = string.IsNullOrWhiteSpace(reason.Text)
                ? "Stopped by user to start a different goal."
                : reason.Text.Trim();
            if (value.Length <= 16 * 1024)
            {
                Result = new(value);
                Close();
            }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Abort “{goal.Title}”?",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "This closes the goal and any paused run, keeps its durable history and worktree, and returns the composer to new-goal mode. It does not delete files or undo changes.",
                    TextWrapping = TextWrapping.Wrap,
                },
                reason,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, abort },
                },
            },
        };
    }

    internal GoalAbortReason? Result { get; private set; }
}

