using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class WorkbenchOverview
{
    private readonly Func<AvaloniaShellState> state;
    private readonly DocumentsHost documents;
    private readonly TextBlock heading = new()
    {
        FontSize = 22,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button secretsAction = new()
    {
        Content = "Project User Secrets",
        IsVisible = false,
    };

    internal WorkbenchOverview(
        Func<AvaloniaShellState> state,
        DocumentsHost documents,
        Func<bool, Task> manageWorkspace,
        Func<Task> manageProjectSecrets)
    {
        this.state = state;
        this.documents = documents;
        Action = new() { Content = "Open workspace" };
        Action.Classes.Add("primary");
        Action.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(Action, "Open or manage workspace");
        Action.Click += async (_, _) => await manageWorkspace(ActiveWorkspace() is null);
        secretsAction.Classes.Add("command");
        secretsAction.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(secretsAction, "Manage project User Secrets");
        secretsAction.Click += async (_, _) => await manageProjectSecrets();
        StackPanel actions = new()
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children = { Action, secretsAction },
        };
        Content = new Grid
        {
            Children =
            {
                new Border
                {
                    MaxWidth = 720,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Classes = { "card" },
                    Child = new StackPanel
                    {
                        Spacing = 14,
                        Children =
                        {
                            new TextBlock { Text = "HARNESS.NET WORKSPACE", Classes = { "eyebrow" } },
                            heading,
                            details,
                            actions,
                        },
                    },
                },
            },
        };
    }

    internal Control Content { get; }
    internal Button Action { get; }

    internal void Update(WorkspaceView? active)
    {
        if (active is null)
        {
            heading.Text = "Open a repository to get started";
            details.Text = "Choose a Git-backed .NET repository. Harness.NET will discover " +
                           "its solutions and projects before asking you to trust it.";
            Action.Content = "Open workspace";
            Action.Classes.Remove("command");
            Action.Classes.Add("primary");
            secretsAction.IsVisible = false;
            return;
        }
        heading.Text = active.Name;
        details.Text = $"{active.RootPath}\n\nBranch: {active.Branch}\n" +
                       $"Trust: {(active.IsTrusted ? "Trusted" : "Not trusted")}\n" +
                       $"Working tree: {(active.IsDirty ? "Has changes" : "Clean")}\n\n" +
                       (active.IsTrusted
                           ? "Use Files or Git to open source and diff documents in this editor."
                           : "Trust this workspace before reading repository content.");
        Action.Content = "Workspace settings";
        Action.Classes.Remove("primary");
        Action.Classes.Add("command");
        secretsAction.IsVisible = active.IsTrusted;
    }

    internal void OpenPlan()
    {
        if (state().Goals.CurrentPlan is not { } plan)
        {
            details.Text = "The selected goal has no current plan to open.";
            documents.ActivateOverview();
            return;
        }
        documents.OpenOrReplace(
            WorkbenchDockIds.PlanDocument,
            $"Plan · revision {plan.Revision.Value}",
            new ScrollViewer
            {
                Content = MarkdownContentView.Create(plan.Content, _ => null),
                Padding = new Thickness(18),
            });
    }

    internal void OpenEvidence()
    {
        if (state().Goals.Workflow?.Evidence is not { Count: > 0 } items)
        {
            details.Text = "The selected goal has no durable workflow evidence to open.";
            documents.ActivateOverview();
            return;
        }
        StackPanel content = new() { Spacing = 14 };
        foreach (var item in items)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{item.Sequence}. {item.Title.Value}",
                FontWeight = FontWeight.SemiBold,
            });
            content.Children.Add(MarkdownContentView.Create(item.Content.Value, _ => null));
        }
        documents.OpenOrReplace(
            WorkbenchDockIds.EvidenceDocument,
            "Workflow evidence",
            new ScrollViewer { Content = content, Padding = new Thickness(18) });
    }

    private WorkspaceView? ActiveWorkspace() =>
        state().Workspaces.Registered.FirstOrDefault(item => item.IsActive);
}
