using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
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
        DateTimeOffset now,
        ToolEvidenceSnapshot? evidence = null)
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
        ToolEvidenceView[] currentTools = evidence?.Items
            .Where(item => goals.SelectedGoalId?.Value == item.GoalId &&
                item.StartedAt >= startedAt)
            .OrderBy(item => item.StartedAt)
            .ToArray() ?? [];
        ToolEvidenceView? runningTool = currentTools.LastOrDefault(item =>
            item.State is ToolEvidenceState.Running);
        DateTimeOffset toolUpdatedAt = currentTools
            .Select(item => item.CompletedAt ?? item.StartedAt)
            .DefaultIfEmpty(startedAt)
            .Max();
        DateTimeOffset updatedAt = new[] { latest?.OccurredAt ?? startedAt, toolUpdatedAt }.Max();
        TimeSpan elapsed = NonNegative(now - startedAt);
        TimeSpan updateAge = NonNegative(now - updatedAt);
        string phase = runningTool is null
            ? Phase(latest?.Kind)
            : $"{ToolLabel(runningTool.Tool)} · running";
        string elapsedText = Duration(elapsed);
        string ageText = updateAge < TimeSpan.FromSeconds(5)
            ? "just now"
            : $"{Duration(updateAge)} ago";
        string operation = goals.WorkflowOperationName ?? "Agent workflow";

        GoalWorkflowActivityView[] currentActivities = goals.Workflow?.Activities
            .Where(activity => activity.OccurredAt >= startedAt)
            .ToArray() ?? [];
        string timeline = currentActivities.Length > 0
            ? string.Join('\n', currentActivities
                .TakeLast(MaximumTimelineItems)
                .Select(activity =>
                    $"{activity.OccurredAt:HH:mm:ss} · {activity.Actor} · " +
                    Bounded(activity.Summary.Value)))
            : "No durable workflow checkpoint has arrived yet.";
        string details = $"{operation}\n{phase}\nElapsed {elapsedText} · " +
            $"last observable update {ageText}\n\nRecent durable activity\n{timeline}";
        if (currentTools.Length > 0)
        {
            details += "\n\nRecent typed operations\n" + string.Join('\n', currentTools
                .TakeLast(MaximumTimelineItems)
                .Select(item => $"{item.StartedAt:HH:mm:ss} · {ToolLabel(item.Tool)} · " +
                    item.State));
        }
        else if (evidence?.Error is { Length: > 0 } error)
        {
            details += $"\n\nTyped operation status unavailable · {Bounded(error)}";
        }
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

    private static string ToolLabel(ToolKind tool) => tool switch
    {
        ToolKind.FileEdit => "File edit",
        ToolKind.Rename => "Rename",
        ToolKind.DocumentTransformation => "Code transformation",
        ToolKind.Build => "Build",
        ToolKind.Test => "Test",
        ToolKind.Restore => "Restore",
        ToolKind.VisualCapture => "Visual capture",
        ToolKind.ToolsetGrant => "Toolset grant",
        _ => tool.ToString(),
    };
}

internal sealed class AgentActivityStatusControl : IDisposable
{
    private readonly IToolEvidenceService evidenceService;
    private readonly Button button = new();
    private readonly TextBlock details = new()
    {
        MaxWidth = 520,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button cancel = new() { Content = "Cancel workflow" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GoalManagementState goals = GoalManagementState.Initial;
    private ToolEvidenceSnapshot? evidence;
    private CancellationTokenSource? evidenceRefresh;
    private int refreshInProgress;

    internal AgentActivityStatusControl(IToolEvidenceService evidenceService)
    {
        this.evidenceService = evidenceService;
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
        timer.Tick += OnTimerTick;
    }

    internal event Action? CancelRequested;

    internal Control Control => button;

    internal void Update(GoalManagementState state)
    {
        bool operationChanged = goals.SelectedGoalId != state.SelectedGoalId ||
            goals.WorkflowOperationStartedAt != state.WorkflowOperationStartedAt;
        if (operationChanged)
        {
            evidenceRefresh?.Cancel();
            evidence = null;
        }
        goals = state;
        Render(TimeProvider.System.GetUtcNow());
        if (operationChanged && state.IsWorkflowRunning)
        {
            _ = RefreshEvidenceAsync();
        }
    }

    public void Dispose()
    {
        timer.Stop();
        evidenceRefresh?.Cancel();
        evidenceRefresh?.Dispose();
    }

    private void Render(DateTimeOffset now)
    {
        AgentActivityStatusView view = AgentActivityStatusProjector.Project(goals, now, evidence);
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

    private async void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        Render(TimeProvider.System.GetUtcNow());
        await RefreshEvidenceAsync();
    }

    private async Task RefreshEvidenceAsync()
    {
        if (!goals.IsWorkflowRunning || goals.SelectedGoalId is null ||
            Interlocked.Exchange(ref refreshInProgress, 1) != 0)
        {
            return;
        }

        GoalId goalId = goals.SelectedGoalId;
        DateTimeOffset? operationStartedAt = goals.WorkflowOperationStartedAt;
        CancellationTokenSource refresh = new();
        evidenceRefresh = refresh;
        try
        {
            ToolEvidenceSnapshot refreshed = await evidenceService.ListAsync(
                goalId.Value, refresh.Token);
            if (!refresh.IsCancellationRequested && IsCurrentOperation(goalId, operationStartedAt))
            {
                evidence = refreshed;
                Render(TimeProvider.System.GetUtcNow());
            }
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentOperation(goalId, operationStartedAt))
            {
                evidence = new([], null, exception.Message);
                Render(TimeProvider.System.GetUtcNow());
            }
        }
        finally
        {
            if (ReferenceEquals(evidenceRefresh, refresh))
            {
                evidenceRefresh = null;
            }
            refresh.Dispose();
            Interlocked.Exchange(ref refreshInProgress, 0);
        }
    }

    private bool IsCurrentOperation(GoalId goalId, DateTimeOffset? operationStartedAt) =>
        goals.IsWorkflowRunning && goals.SelectedGoalId == goalId &&
        goals.WorkflowOperationStartedAt == operationStartedAt;
}
