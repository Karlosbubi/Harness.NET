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

internal sealed class RestoreApprovalDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly GoalView goal;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly ListBox approvals = new();
    private readonly TextBox details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button request = new() { Content = "New correlated request…" };
    private readonly Button approve = new() { Content = "Approve once…" };
    private readonly Button deny = new() { Content = "Deny…" };
    private bool rendering;

    internal RestoreApprovalDialog(
        AvaloniaPresentationStore store,
        GoalView goal,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.goal = goal;
        this.cancellationToken = cancellationToken;
        Title = "Restore approvals";
        Width = 900;
        Height = 650;
        MinWidth = 700;
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
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        Grid root = new()
        {
            ColumnDefinitions = new("320,*"),
            RowDefinitions = new("Auto,*,Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 10,
            Margin = new Thickness(20),
        };
        TextBlock heading = new()
        {
            Text = "Correlation-bound restore authorization",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetColumnSpan(heading, 2);
        root.Children.Add(heading);
        Grid.SetRow(approvals, 1);
        root.Children.Add(approvals);
        Grid.SetRow(details, 1);
        Grid.SetColumn(details, 1);
        root.Children.Add(details);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { request, approve, deny },
        };
        Grid.SetRow(actions, 2);
        Grid.SetColumnSpan(actions, 2);
        root.Children.Add(actions);
        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        Grid.SetColumnSpan(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        approvals.SelectionChanged += (_, _) =>
        {
            if (!rendering && approvals.SelectedItem is RestoreApprovalChoice choice)
            {
                details.Text = Format(choice.Approval);
                SetDecisionButtons(choice.Approval, store.Current.Goals.IsBusy);
            }
        };
        request.Click += async (_, _) =>
        {
            RestoreRequestDialog dialog = new();
            await dialog.ShowDialog(this);
            if (dialog.Result is { } result)
            {
                await store.RequestRestoreApprovalAsync(
                    goal.Id,
                    result.CorrelationId,
                    result.Rationale,
                    cancellationToken);
            }
        };
        approve.Click += async (_, _) =>
        {
            if (approvals.SelectedItem is not RestoreApprovalChoice choice)
            {
                return;
            }

            RestoreDecisionConfirmationDialog confirmation = new(choice.Approval);
            if (await confirmation.ShowDialog<bool>(this))
            {
                await store.DecideRestoreApprovalAsync(
                    goal.Id,
                    choice.Approval.Id,
                    CapabilityDecision.Approve,
                    reason: null,
                    cancellationToken);
            }
        };
        deny.Click += async (_, _) =>
        {
            if (approvals.SelectedItem is not RestoreApprovalChoice choice)
            {
                return;
            }

            TextEntryDialog reason = new(
                "Deny restore request",
                "Required reason",
                "Deny request",
                "A denial reason is required.");
            await reason.ShowDialog(this);
            if (reason.Result is not null)
            {
                await store.DecideRestoreApprovalAsync(
                    goal.Id,
                    choice.Approval.Id,
                    CapabilityDecision.Deny,
                    reason.Result,
                    cancellationToken);
            }
        };
    }

    private void Render(GoalManagementState state)
    {
        rendering = true;
        try
        {
            RestoreApprovalChoice[] items = state.CapabilityApprovals
                .Select(item => new RestoreApprovalChoice(item))
                .ToArray();
            string? selectedId = (approvals.SelectedItem as RestoreApprovalChoice)?.Approval.Id.Value;
            approvals.ItemsSource = items;
            approvals.SelectedItem = items.FirstOrDefault(item =>
                item.Approval.Id.Value == selectedId) ?? items.FirstOrDefault();
            CapabilityApprovalView? selected =
                (approvals.SelectedItem as RestoreApprovalChoice)?.Approval;
            details.Text = selected is null
                ? "No restore approval requests. Create one only for a known restore correlation."
                : Format(selected);
            request.IsEnabled = !state.IsBusy;
            SetDecisionButtons(selected, state.IsBusy);
            status.Text = state.IsBusy ? "Updating durable approval…" : state.Status ?? string.Empty;
        }
        finally
        {
            rendering = false;
        }
    }

    private void SetDecisionButtons(CapabilityApprovalView? approval, bool busy)
    {
        bool pending = approval?.State is CapabilityApprovalState.Pending;
        approve.IsEnabled = !busy && pending;
        deny.IsEnabled = !busy && pending;
    }

    private static string Format(CapabilityApprovalView approval) => string.Join(
        '\n',
        $"State: {approval.State}",
        $"Capability: {approval.Capability}",
        $"Goal: {approval.GoalId}",
        $"Correlation: {approval.CorrelationId.Value}",
        $"Exact target: {approval.Target}",
        $"Requested: {approval.RequestedAt:O}",
        approval.DecidedAt is null ? string.Empty : $"Decided: {approval.DecidedAt:O}",
        string.Empty,
        "Rationale:",
        approval.Rationale,
        approval.DecisionReason is null ? string.Empty : $"\nDecision reason:\n{approval.DecisionReason}");

    private sealed record RestoreApprovalChoice(CapabilityApprovalView Approval)
    {
        public override string ToString() =>
            $"{Approval.State} — {Approval.CorrelationId.Value}";
    }
}

