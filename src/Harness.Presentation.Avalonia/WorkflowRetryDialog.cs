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

internal sealed record WorkflowRetryResult(
    GoalModelCandidate Model,
    string? Guidance);

internal sealed class WorkflowRetryDialog : Window
{
    private readonly SearchableModelPicker models = new() { MinWidth = 420 };
    private readonly TextBox guidance = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 110,
        PlaceholderText = "Optional: add guidance for this retry",
    };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal WorkflowRetryDialog(
        GoalWorkflowRetryRole role,
        IReadOnlyList<GoalModelCandidate> candidates,
        GoalModelCandidate? selected,
        string disclosure)
    {
        Title = $"Retry {role} with changes";
        Width = 780;
        Height = 660;
        MinWidth = 640;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AgentRole agentRole = role switch
        {
            GoalWorkflowRetryRole.Lead => AgentRole.Lead,
            GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
            GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        GoalModelCandidate[] choices = ModelSelectionCatalog.ForRole(candidates, agentRole);
        models.SetCandidates(choices, selected);
        models.SetAutomationName($"Replacement model for {role} retry");
        AutomationProperties.SetName(guidance, $"Guidance for {role} retry");
        AutomationProperties.SetName(validation, "Retry validation");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button retry = new() { Content = $"Retry {role}", IsEnabled = choices.Length > 0 };
        retry.Classes.Add("primary");
        retry.Click += (_, _) => Save();
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
                        Text = $"Retry failed {role} call",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new Border
                    {
                        Classes = { "card", "attention" },
                        Child = new TextBlock { Text = disclosure, TextWrapping = TextWrapping.Wrap },
                    },
                    new TextBlock { Text = "Replacement model", FontWeight = FontWeight.SemiBold },
                    models,
                    new TextBlock
                    {
                        Text = "Only models that fully support this role are shown. The current route is selected when it remains available.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "Additional guidance (optional)", FontWeight = FontWeight.SemiBold },
                    guidance,
                    validation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, retry },
                    },
                },
            },
        };
        Opened += (_, _) => models.Focus();
    }

    internal WorkflowRetryResult? Result { get; private set; }

    private void Save()
    {
        if (models.SelectedCandidate is not { } candidate)
        {
            validation.Text = "No compatible replacement model is available.";
            return;
        }

        string direction = guidance.Text?.Trim() ?? string.Empty;
        if (direction.Length > 16 * 1024)
        {
            validation.Text = "Optional retry guidance may contain at most 16384 characters.";
            return;
        }

        Result = new(candidate, direction.Length == 0 ? null : direction);
        Close();
    }
}

