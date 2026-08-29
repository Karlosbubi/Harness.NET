using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow : Window
{
    private void Render(AvaloniaShellState state)
    {
        commandBar.Content = BuildCommandBar();
        suppressSelection = true;
        try
        {
            composer.Text = state.ComposerText;
            composer.IsEnabled = !state.IsLoading;
            bool createsGoal = state.Goals.SelectedGoal is null;
            composer.PlaceholderText = createsGoal
                ? "Describe the goal you want Harness to pursue"
                : "Message Harness about the selected goal";
            send.Content = createsGoal ? "Create goal" : "Send";
            // Automation edits synchronize at click; the store still rejects empty or active submissions.
            send.IsEnabled = true;
            cancel.IsVisible = state.IsStreaming;
            agentActivityStatus.Update(state.Goals);
            inboundMcpIndicator.IsVisible = state.Settings.InboundMcpSettings?.Status.IsRunning == true;
            if (state.Settings.InboundMcpSettings?.Status is { IsRunning: true } inbound)
            {
                inboundMcpIndicator.Content = $"MCP · {inbound.ActiveClients.Count}";
                ToolTip.SetTip(inboundMcpIndicator,
                    $"Authenticated local control active\n{inbound.Endpoint}\nInstance {inbound.InstanceId}");
            }
            manageFramework.IsEnabled = !state.IsLoading &&
                                        state.Workspaces.Registered.Any(item => item.IsActive);
            inspectGoalContext.IsEnabled = !state.IsLoading && state.Goals.SelectedGoal is not null;
            DashboardSnapshot? dashboard = state.Dashboard;
            if (dashboard is not null)
            {
                bool hasWorkspace = state.Workspaces.Registered.Any(item => item.IsActive);
                workspace.Text = hasWorkspace
                    ? $"{dashboard.Workspace.Name}\n{dashboard.Workspace.Branch}\n{dashboard.Workspace.Trust}"
                    : "No workspace open\nChoose a Git-backed .NET repository to begin.";
                brandDetail.Text = hasWorkspace
                    ? $"{dashboard.Workspace.Name} · {dashboard.Workspace.Branch}"
                    : "No workspace open";
                manageWorkspaces.Content = hasWorkspace ? "Switch workspace…" : "Open workspace…";
                RenderActivities(state);
                ToolTip.SetTip(modelPicker, ProviderText(dashboard.Provider));
                RenderGoalInspector(state.Goals);
                string[] models = dashboard.Provider.Models.Select(model => model.Id).ToArray();
                modelPicker.ItemsSource = models;
                modelPicker.SelectedItem = models.FirstOrDefault(model =>
                    model == dashboard.Provider.SelectedModel);
                status.Message = state.Error is not null
                    ? $"Error: {state.Error}"
                    : state.IsStreaming ? "Streaming response" : dashboard.Status;
                status.Severity = state.Error is not null
                    ? StatusSeverity.Error
                    : state.IsStreaming ? StatusSeverity.Information : StatusSeverity.Success;
                budget.Text = dashboard.Budget;
            }
            else
            {
                status.Message = state.Error ?? "Loading";
                status.Severity = state.Error is null
                    ? StatusSeverity.Information
                    : StatusSeverity.Error;
            }

            if (state.Appearance is { } appearance)
            {
                themeController.Register(AvaloniaThemeMapper.UserThemes(appearance));
                themeController.Select(new(appearance.EffectiveThemeId.Value));
            }
            workbench?.Update(state);
        }
        finally
        {
            suppressSelection = false;
        }

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        header.Background = Brush(UiThemeColorToken.Header);
        header.BorderBrush = Brush(UiThemeColorToken.Border);
        header.BorderThickness = new Thickness(0, 0, 0, 1);
        navigation.Background = Brush(UiThemeColorToken.Panel);
        primary.Background = Brush(UiThemeColorToken.Editor);
        utility.Background = Brush(UiThemeColorToken.Panel);
        footer.Background = Brush(UiThemeColorToken.Header);
        footer.BorderBrush = Brush(UiThemeColorToken.Border);
        footer.BorderThickness = new Thickness(0, 1, 0, 0);
        Background = Brush(UiThemeColorToken.Window);
        Foreground = Brush(UiThemeColorToken.TextPrimary);
        status.RefreshTheme();
    }

    private void RenderActivities(AvaloniaShellState state)
    {
        List<Control> timeline = state.Dashboard?.Activities
            .Select(CreateMessageCard)
            .ToList() ?? [];
        if (state.Goals.SelectedGoal is null && state.Goals.Items.Count > 0)
        {
            timeline.Add(new TextBlock
            {
                Text = "CONTINUE A GOAL",
                Classes = { "eyebrow" },
                Margin = new Thickness(6, 8, 6, 6),
            });
            timeline.AddRange(state.Goals.Items.Select(CreateGoalChoice));
        }
        IReadOnlyList<ConversationWorkflowCard> workflow =
            ConversationWorkflowProjector.Project(state.Goals, state.Error);
        if (workflow.Count > 0)
        {
            timeline.Add(new TextBlock
            {
                Text = "GOAL TIMELINE",
                Classes = { "eyebrow" },
                Margin = new Thickness(6, 8, 6, 6),
            });
            timeline.AddRange(workflow.Select(CreateWorkflowCard));
        }
        activities.ItemsSource = timeline;
        Dispatcher.UIThread.Post(conversationScroll.ScrollToEnd);
    }

    private Control CreateGoalChoice(GoalView goal)
    {
        Button select = new()
        {
            Content = "Continue",
            Classes = { "command" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(select, $"Continue goal {goal.Title}");
        select.Click += async (_, _) => await store.SelectGoalAsync(goal.Id, cancellationToken);

        Button abort = new()
        {
            Content = "Abort",
            Classes = { "command" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(abort, $"Abort goal {goal.Title} and start new");
        abort.Click += async (_, _) => await AbortGoalAsync(goal);

        Grid heading = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 10 };
        heading.Children.Add(new TextBlock
        {
            Text = goal.Title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(select, 1);
        heading.Children.Add(select);
        Grid.SetColumn(abort, 2);
        heading.Children.Add(abort);
        Border card = new()
        {
            Classes = { "workflow-card" },
            Margin = new Thickness(4, 0, 28, 9),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    heading,
                    new TextBlock
                    {
                        Text = goal.Objective,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 52,
                    },
                },
            },
        };
        AutomationProperties.SetName(card, $"Available goal: {goal.Title}, {goal.State}");
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Content);
        return card;
    }

    private Control CreateMessageCard(ActivityItem item)
    {
        bool isUser = string.Equals(item.Actor, "You", StringComparison.Ordinal);
        TextBlock actor = new()
        {
            Text = item.Actor,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
        };
        Control content = MarkdownContentView.Create(item.Summary, Brush);
        content.Margin = new Thickness(0, 5, 0, 0);
        TextBlock messageStatus = new()
        {
            Text = item.Status,
            FontSize = 11,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid metadata = new() { ColumnDefinitions = new("*,Auto") };
        metadata.Children.Add(actor);
        Grid.SetColumn(messageStatus, 1);
        metadata.Children.Add(messageStatus);
        StackPanel body = new() { Children = { metadata, content } };
        Border card = new()
        {
            Child = body,
            Padding = new Thickness(13, 10),
            Margin = isUser ? new Thickness(52, 0, 4, 10) : new Thickness(4, 0, 52, 10),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
        };
        card.Classes.Add("message-card");
        if (isUser)
        {
            card.Classes.Add("user");
        }

        return card;
    }

    private void RenderGoalInspector(GoalManagementState goals)
    {
        if (goals.SelectedGoal is not { } selected)
        {
            goalContext.Text = "No goal selected\nOpen Goals and plans to select or create one.";
            evidence.ItemsSource = Array.Empty<Control>();
            return;
        }

        string plan = goals.CurrentPlan is null
            ? "No plan proposed"
            : $"Plan revision {goals.CurrentPlan.Revision.Value} · {goals.CurrentPlan.State}";
        string workflow = goals.Workflow is null
            ? "No workflow started"
            : $"Workflow {goals.Workflow.State} · {goals.Workflow.Tasks.Count} task(s)";
        goalContext.Text = $"{selected.Title}\n{selected.State}\n\n{selected.Objective}\n\n" +
                           $"{plan}\n{workflow}";

        evidence.ItemsSource = goals.Workflow?.Evidence.Count > 0
            ? goals.Workflow.Evidence
                .Select(item => CreateEvidenceCard(item.Title.Value, item.Content.Value))
                .ToArray()
            : new Control[]
            {
                new TextBlock
                {
                    Text = "No durable workflow evidence exists for this goal yet.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                },
            };
    }

    private Control CreateEvidenceCard(string title, string content) => new Border
    {
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(7),
        Background = Brush(UiThemeColorToken.Raised),
        BorderBrush = Brush(UiThemeColorToken.Border),
        BorderThickness = new Thickness(1),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                MarkdownContentView.Create(content, Brush),
            },
        },
    };

    private static string ProviderText(ProviderSnapshot provider)
    {
        string selected = string.IsNullOrWhiteSpace(provider.SelectedModel)
            ? "No model selected"
            : provider.SelectedModel;
        string catalog = provider.Models.Count == 0
            ? "No models discovered"
            : $"{provider.Models.Count} model(s) available";
        return $"{provider.Name}\n{provider.Health}\n{catalog}\nSelected: {selected}" +
               (provider.Error is null ? string.Empty : $"\n{provider.Error}");
    }

    private static IBrush? Brush(UiThemeColorToken token) =>
        Application.Current?.TryFindResource(HarnessThemeResources.Key(token), out object? value) is true
            ? value as IBrush
            : null;

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> items = [];

        internal void Add(IDisposable disposable) => items.Add(disposable);

        public void Dispose()
        {
            foreach (IDisposable item in items)
            {
                item.Dispose();
            }

            items.Clear();
        }
    }
}
