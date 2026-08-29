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

internal sealed class RemoteModelAuthorizationDialog : Window
{
    internal RemoteModelAuthorizationDialog(
        GoalView goal,
        GoalModelCandidate candidate,
        AgentRole role)
    {
        Title = "Authorize remote model";
        Width = 620;
        Height = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string pricing = candidate.InputPrice is null || candidate.OutputPrice is null
            ? "Published pricing is unavailable. Inference will fail closed until pricing is known."
            : $"Published rates: input ${candidate.InputPrice.Value:0.######}/M tokens, " +
              $"output ${candidate.OutputPrice.Value:0.######}/M tokens" +
              (candidate.RequestPrice?.Value > 0
                  ? $", request ${candidate.RequestPrice.Value:0.######}."
                  : ".");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        RemoteSpendPreference spend = RemoteSpendPreference.FromGoalBudget(goal.RemoteBudget);
        bool remoteAuthorized = spend.Mode is not RemoteSpendMode.LocalOnly;
        Button authorize = new()
        {
            Content = remoteAuthorized ? "Use remote model" : "Enable remote spend first",
            IsEnabled = remoteAuthorized,
        };
        authorize.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"Use {candidate.Provider.Value}/{candidate.Model.Value} for {role}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = spend.Mode switch
                    {
                        RemoteSpendMode.Unlimited => "Goal spend mode: Unlimited. " + pricing,
                        RemoteSpendMode.Capped => $"Goal cap: ${GoalPresentationFormatter.ToUsd(spend.Cap!.Value)}. " + pricing,
                        _ => "This goal is currently local-only. Choose unlimited or capped remote spend in Goal settings before selecting this route. " + pricing,
                    },
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Every request reserves a conservative maximum before inference, is " +
                           "attributed to this goal, and fails closed when pricing or the selected spend policy disallows it.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, authorize },
                },
            },
        };
    }
}

