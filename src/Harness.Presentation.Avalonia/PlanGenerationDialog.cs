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

internal sealed record PlanGenerationResult(GoalModelCandidate LeadModel);

internal sealed class PlanGenerationDialog : Window
{
    private readonly SearchableModelPicker models = new() { MinWidth = 380 };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal PlanGenerationDialog(
        IReadOnlyList<GoalModelCandidate> candidates,
        GoalModelCandidate? selected,
        string disclosure)
    {
        Title = "Generate goal plan";
        Width = 760;
        Height = 570;
        MinWidth = 620;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GoalModelCandidate[] choices = ModelSelectionCatalog.ForRole(
            candidates, AgentRole.Lead);
        models.SetCandidates(choices, selected);
        models.SetAutomationName("Lead model for plan generation");
        AutomationProperties.SetName(validation, "Plan generation validation");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button run = new() { Content = "Generate plan" };
        run.Classes.Add("primary");
        run.IsEnabled = choices.Length > 0;
        run.Click += (_, _) => Save();
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Lead plan generation",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock { Text = disclosure, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Lead model", FontWeight = FontWeight.SemiBold },
                    models,
                    new TextBlock
                    {
                        Text = "Only chat models declaring every Lead capability are shown. " +
                               "The configured Lead route is selected by default.",
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                    validation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, run },
                    },
                },
            },
        };
    }

    internal PlanGenerationResult? Result { get; private set; }

    private void Save()
    {
        if (models.SelectedCandidate is not { } candidate)
        {
            validation.Text = "No fully compatible Lead model is available.";
            return;
        }

        Result = new(candidate);
        Close();
    }
}

