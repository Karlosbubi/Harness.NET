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

internal sealed class PlanApprovalDialog : Window
{
    internal PlanApprovalDialog(GoalView goal, PlanView plan)
    {
        Title = "Approve plan and capabilities";
        Width = 680;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        TextEditor planContent = CodeEditorView.Create(
            plan.Content,
            wordWrap: true,
            showLineNumbers: false);
        planContent.MinHeight = 260;
        AutomationProperties.SetName(
            planContent,
            $"Plan revision {plan.Revision.Value} content");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button approve = new() { Content = "Approve and create worktree" };
        approve.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"Approve {goal.Title} — plan revision {plan.Revision.Value}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Approval creates an isolated branch and worktree and grants the goal " +
                           "repository-local inspection, edit, build, and test capabilities. " +
                           "Restore, network access, destructive actions, and commits remain " +
                           "separately approval-gated.",
                    TextWrapping = TextWrapping.Wrap,
                },
                planContent,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, approve },
                },
            },
        };
    }
}

