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

internal sealed class CommitApprovalDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly TextBox fingerprint = Viewer();
    private readonly TextEditor diff = CodeEditorView.Create();
    private readonly TextBox message = new();
    private readonly TextBox authorName = new();
    private readonly TextBox authorEmail = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel requestFields = new() { Spacing = 6 };
    private readonly Button request = new() { Content = "Record pending request" };
    private readonly Button approve = new() { Content = "Approve exact diff…" };
    private readonly Button deny = new() { Content = "Deny…" };
    private readonly Button resume = new() { Content = "Resume approved commit…" };

    internal CommitApprovalDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        Title = "Exact commit approval";
        Width = 1040;
        Height = 760;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Goals)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        AutomationProperties.SetName(fingerprint, "Exact commit fingerprint");
        AutomationProperties.SetName(message, "Commit message");
        AutomationProperties.SetName(authorName, "Commit author name");
        AutomationProperties.SetName(authorEmail, "Commit author email");
        AutomationProperties.SetName(status, "Commit operation status");
        requestFields.Children.Add(new TextBlock { Text = "Commit message" });
        requestFields.Children.Add(message);
        requestFields.Children.Add(new TextBlock { Text = "Author name" });
        requestFields.Children.Add(authorName);
        requestFields.Children.Add(new TextBlock { Text = "Author email" });
        requestFields.Children.Add(authorEmail);
        requestFields.Children.Add(request);

        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        Grid root = new()
        {
            RowDefinitions = new("Auto,120,*,Auto,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
        };
        root.Children.Add(new TextBlock
        {
            Text = "User-owned exact-diff commit",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        Grid.SetRow(fingerprint, 1);
        root.Children.Add(fingerprint);
        Grid.SetRow(diff, 2);
        AutomationProperties.SetName(diff, "Complete commit diff");
        root.Children.Add(diff);
        Grid.SetRow(requestFields, 3);
        root.Children.Add(requestFields);
        StackPanel decisions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { approve, deny, resume },
        };
        Grid.SetRow(decisions, 4);
        root.Children.Add(decisions);
        Grid footer = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        request.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(message.Text) ||
                string.IsNullOrWhiteSpace(authorName.Text) ||
                string.IsNullOrWhiteSpace(authorEmail.Text))
            {
                status.Text = "Commit message, author name, and author email are required.";
                return;
            }

            await store.RequestCommitApprovalAsync(
                new(message.Text),
                new(authorName.Text),
                new(authorEmail.Text),
                cancellationToken);
        };
        approve.Click += async (_, _) => await ConfirmAndCommitAsync(resuming: false);
        resume.Click += async (_, _) => await ConfirmAndCommitAsync(resuming: true);
        deny.Click += async (_, _) =>
        {
            TextEntryDialog reason = new(
                "Deny exact commit",
                "Required reason",
                "Deny commit",
                "A denial reason is required.");
            await reason.ShowDialog(this);
            if (reason.Result is not null)
            {
                await store.DecideCommitAsync(
                    GoalCommitDecision.Deny,
                    new(reason.Result),
                    cancellationToken);
            }
        };
    }

    private async Task ConfirmAndCommitAsync(bool resuming)
    {
        GoalCommitApprovalView? approval = store.Current.Goals.CommitApproval;
        if (approval is null)
        {
            return;
        }

        ExactCommitConfirmationDialog confirmation = new(approval, resuming);
        if (await confirmation.ShowDialog<bool>(this))
        {
            await store.DecideCommitAsync(
                GoalCommitDecision.Approve,
                reason: null,
                cancellationToken);
        }
    }

    private void Render(GoalManagementState state)
    {
        GoalCommitPreview? preview = state.CommitPreview;
        GoalCommitApprovalView? approval = state.CommitApproval;
        fingerprint.Text = GoalPresentationFormatter.FormatCommitFingerprint(preview, approval);
        diff.Text = approval?.Diff.Value ?? preview?.Diff.Value ?? "No exact diff is available.";
        bool busy = state.IsBusy;
        requestFields.IsVisible = preview is not null && approval is null;
        request.IsEnabled = !busy && preview is not null && approval is null;
        approve.IsVisible = approval?.State is GoalCommitApprovalState.Pending;
        approve.IsEnabled = !busy && approval?.State is GoalCommitApprovalState.Pending;
        deny.IsVisible = approval?.State is GoalCommitApprovalState.Pending;
        deny.IsEnabled = !busy && approval?.State is GoalCommitApprovalState.Pending;
        resume.IsVisible = approval?.State is GoalCommitApprovalState.Approved;
        resume.IsEnabled = !busy && approval?.State is GoalCommitApprovalState.Approved;
        status.Text = busy ? "Revalidating exact commit state…" : state.Status ?? string.Empty;
    }

    private static TextBox Viewer() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
    };
}

