using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Workflows;

namespace Harness.Presentation.Avalonia;

internal sealed record AgentActivityStatusView(
    bool IsVisible,
    string CompactText,
    string AccessibleName,
    string Details)
{
    internal static AgentActivityStatusView Hidden { get; } = new(
        IsVisible: false,
        string.Empty,
        "No agent workflow is active.",
        string.Empty);
}

internal static class AgentActivityStatusProjector
{
    private const int MaximumTimelineItems = 8;
    private const int MaximumSummaryCharacters = 240;

    internal static AgentActivityStatusView Project(
        GoalManagementState goals,
        DateTimeOffset now)
    {
        if (!goals.IsWorkflowRunning)
        {
            return AgentActivityStatusView.Hidden;
        }

        GoalWorkflowActivityView? latest = goals.Workflow?.Activities
            .LastOrDefault(activity =>
                goals.WorkflowOperationStartedAt is null ||
                activity.OccurredAt >= goals.WorkflowOperationStartedAt.Value);
        DateTimeOffset startedAt = goals.WorkflowOperationStartedAt ??
            latest?.OccurredAt ?? now;
        DateTimeOffset updatedAt = latest?.OccurredAt ?? startedAt;
        TimeSpan elapsed = NonNegative(now - startedAt);
        TimeSpan updateAge = NonNegative(now - updatedAt);
        string phase = Phase(latest?.Kind);
        string elapsedText = Duration(elapsed);
        string ageText = updateAge < TimeSpan.FromSeconds(5)
            ? "just now"
            : $"{Duration(updateAge)} ago";
        string operation = goals.WorkflowOperationName ?? "Agent workflow";

        string timeline = goals.Workflow?.Activities.Count > 0
            ? string.Join('\n', goals.Workflow.Activities
                .TakeLast(MaximumTimelineItems)
                .Select(activity =>
                    $"{activity.OccurredAt:HH:mm:ss} · {activity.Actor} · " +
                    Bounded(activity.Summary.Value)))
            : "No durable workflow checkpoint has arrived yet.";
        string details = $"{operation}\n{phase}\nElapsed {elapsedText} · " +
            $"last observable update {ageText}\n\nRecent durable activity\n{timeline}";
        string accessibleName = $"Agent activity: {phase}. Elapsed {elapsedText}. " +
            $"Last observable update {ageText}. Activate for details.";

        return new(
            IsVisible: true,
            $"{phase} · {elapsedText}",
            accessibleName,
            details);
    }

    private static string Phase(GoalWorkflowCheckpointKind? kind) => kind switch
    {
        GoalWorkflowCheckpointKind.LeadCallStarted => "Lead · waiting for model",
        GoalWorkflowCheckpointKind.ImplementerCallStarted => "Implementer · waiting for model",
        GoalWorkflowCheckpointKind.ReviewerCallStarted => "Reviewer · waiting for model",
        GoalWorkflowCheckpointKind.PlanApproved => "Preparing implementation",
        GoalWorkflowCheckpointKind.ImplementationProduced => "Validating changes",
        GoalWorkflowCheckpointKind.ReviewCompleted => "Applying review decision",
        GoalWorkflowCheckpointKind.PlanProposed => "Plan ready",
        GoalWorkflowCheckpointKind.UserDirectionRequired => "Waiting for direction",
        GoalWorkflowCheckpointKind.Accepted => "Accepted",
        GoalWorkflowCheckpointKind.Started => "Starting workflow",
        _ => "Starting workflow",
    };

    private static TimeSpan NonNegative(TimeSpan value) => value < TimeSpan.Zero
        ? TimeSpan.Zero
        : value;

    private static string Duration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
        : duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s"
            : $"{duration.Seconds}s";

    private static string Bounded(string summary) =>
        summary.Length <= MaximumSummaryCharacters
            ? summary
            : summary[..MaximumSummaryCharacters] + "…";
}

internal sealed class AgentActivityStatusControl : IDisposable
{
    private readonly Button button = new();
    private readonly TextBlock details = new()
    {
        MaxWidth = 520,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button cancel = new() { Content = "Cancel workflow" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GoalManagementState goals = GoalManagementState.Initial;

    internal AgentActivityStatusControl()
    {
        button.Classes.Add("command");
        button.IsVisible = false;
        button.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(8),
                Children = { details, cancel },
            },
        };
        AutomationProperties.SetName(button, "Agent activity status");
        AutomationProperties.SetName(details, "Current agent activity details");
        AutomationProperties.SetName(cancel, "Cancel active agent workflow");
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        timer.Tick += (_, _) => Render(TimeProvider.System.GetUtcNow());
    }

    internal event Action? CancelRequested;

    internal Control Control => button;

    internal void Update(GoalManagementState state)
    {
        goals = state;
        Render(TimeProvider.System.GetUtcNow());
    }

    public void Dispose() => timer.Stop();

    private void Render(DateTimeOffset now)
    {
        AgentActivityStatusView view = AgentActivityStatusProjector.Project(goals, now);
        button.IsVisible = view.IsVisible;
        button.Content = view.CompactText;
        details.Text = view.Details;
        AutomationProperties.SetName(button, view.AccessibleName);
        ToolTip.SetTip(button, view.AccessibleName);
        cancel.IsEnabled = view.IsVisible;
        if (view.IsVisible && !timer.IsEnabled)
        {
            timer.Start();
        }
        else if (!view.IsVisible && timer.IsEnabled)
        {
            timer.Stop();
        }
    }
}
