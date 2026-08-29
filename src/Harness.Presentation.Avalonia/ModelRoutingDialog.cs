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

internal sealed class ModelRoutingDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly SearchableModelPicker candidates = new();
    private readonly TextBox selections = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 100,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button lead = new() { Content = "Use for Lead" };
    private readonly Button implementer = new() { Content = "Use for Implementer" };
    private readonly Button reviewer = new() { Content = "Use for Reviewer" };

    internal ModelRoutingDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Goal role models";
        Width = 900;
        Height = 680;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        candidates.SetAutomationName("Available goal model");
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        return new Grid
        {
            RowDefinitions = new("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "Per-role model routing",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                AtRow(new TextBlock
                {
                    Text = "Catalog discovery performs no inference. Remote selections authorize only " +
                           "the selected provider/model for this goal and remain bounded by its cap.",
                    TextWrapping = TextWrapping.Wrap,
                }, 1),
                AtRow(candidates, 2),
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { lead, implementer, reviewer },
                }, 3),
                AtRow(selections, 4),
                AtRow(new Grid
                {
                    ColumnDefinitions = new("*,Auto"),
                    Children = { status, AtColumn(close, 1) },
                }, 5),
            },
        };
    }

    private void WireInteractions()
    {
        candidates.SelectionChanged += (_, _) => UpdateRoleButtons();
        lead.Click += async (_, _) => await SelectAsync(AgentRole.Lead);
        implementer.Click += async (_, _) => await SelectAsync(AgentRole.Implementer);
        reviewer.Click += async (_, _) => await SelectAsync(AgentRole.Reviewer);
    }

    private async Task SelectAsync(AgentRole role)
    {
        if (candidates.SelectedCandidate is not { } candidate)
        {
            status.Text = "Select a model.";
            return;
        }
        if (candidate.Access is ModelAccess.Remote)
        {
            if (goal.RemoteBudget is null)
            {
                status.Text = "This goal is local-only. Choose unlimited or capped remote spend to authorize remote models.";
                return;
            }

            RemoteModelAuthorizationDialog confirmation = new(goal, candidate, role);
            if (!await confirmation.ShowDialog<bool>(this))
            {
                return;
            }
        }

        await store.SelectGoalModelAsync(goal.Id, role, candidate, cancellationToken);
    }

    private void Render(GoalManagementState state)
    {
        GoalModelCatalog? catalog = state.ModelCatalog;
        candidates.SetCandidates(catalog?.Models ?? []);
        selections.Text = GoalPresentationFormatter.FormatSelections(state.ModelSelections);
        UpdateRoleButtons();
        status.Text = state.IsBusy
            ? "Working…"
            : state.Status ?? catalog?.Error ?? string.Empty;
    }

    private void UpdateRoleButtons()
    {
        GoalModelCandidate? selected = candidates.SelectedCandidate;
        bool enabled = !store.Current.Goals.IsBusy && selected is not null;
        lead.IsEnabled = enabled && selected?.SupportedRoles.Contains(AgentRole.Lead) is true;
        implementer.IsEnabled = enabled &&
            selected?.SupportedRoles.Contains(AgentRole.Implementer) is true;
        reviewer.IsEnabled = enabled && selected?.SupportedRoles.Contains(AgentRole.Reviewer) is true;
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T AtColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

}

