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

internal sealed class SemanticRebuildConfirmationDialog : Window
{
    internal SemanticRebuildConfirmationDialog(
        GoalView goal,
        SemanticIndexStatusResult status,
        RemoteCostReport? cost)
    {
        Title = "Confirm semantic rebuild";
        Width = 680;
        Height = 390;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button rebuild = new() { Content = "Rebuild semantic index" };
        rebuild.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"Rebuild with {status.Profile.Access} " +
                           $"{status.Profile.Provider.Value}/{status.Profile.Model.Value}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Dimensions: {status.Profile.Dimensions.Value}; chunking version: " +
                           $"{status.Profile.ChunkingVersion.Value}. The final input size depends " +
                           "on eligible Git-tracked text.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = GoalPresentationFormatter.FormatCostSummary(goal, cost),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Remote batches use strict no-collection and zero-data-retention routing, " +
                           "remain goal-attributed, and fail closed at the cap. Cancellation preserves " +
                           "the previous ready partition.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, rebuild },
                },
            },
        };
    }
}

