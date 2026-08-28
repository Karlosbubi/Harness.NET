using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
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
        ToolEvidenceSnapshot? evidence = null,
        AgentActivitySnapshot? sessionActivity = null)
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
        GoalWorkflowActivityView[] currentActivities = goals.Workflow?.Activities
            .Where(activity => activity.OccurredAt >= startedAt)
            .ToArray() ?? [];
        bool isRetry = latest?.Kind is GoalWorkflowCheckpointKind.ImplementerCallStarted &&
            currentActivities.Any(activity =>
                activity.Kind is GoalWorkflowCheckpointKind.ReviewCompleted);
        ToolEvidenceView[] currentTools = evidence?.Items
            .Where(item => goals.SelectedGoalId?.Value == item.GoalId &&
                item.StartedAt >= startedAt)
            .OrderBy(item => item.StartedAt)
            .ToArray() ?? [];
        ToolEvidenceView? runningTool = currentTools.LastOrDefault(item =>
            item.State is ToolEvidenceState.Running);
        AgentActivityView[] currentSession = sessionActivity?.Items
            .Where(item => item.GoalId == goals.SelectedGoalId && item.StartedAt >= startedAt)
            .OrderBy(item => item.StartedAt)
            .ToArray() ?? [];
        AgentActivityView[] activeSession = currentSession
            .Where(item => IsActive(item.Phase))
            .ToArray();
        DateTimeOffset toolUpdatedAt = currentTools
            .Select(item => item.CompletedAt ?? item.StartedAt)
            .DefaultIfEmpty(startedAt)
            .Max();
        DateTimeOffset sessionUpdatedAt = currentSession
            .Select(item => item.UpdatedAt)
            .DefaultIfEmpty(startedAt)
            .Max();
        DateTimeOffset updatedAt = new[]
        {
            latest?.OccurredAt ?? startedAt,
            toolUpdatedAt,
            sessionUpdatedAt,
        }.Max();
        TimeSpan elapsed = NonNegative(now - startedAt);
        TimeSpan updateAge = NonNegative(now - updatedAt);
        string[] activeLabels = ActiveLabels(activeSession, currentTools, isRetry);
        string phase = activeLabels.Length switch
        {
            > 1 => $"{activeLabels.Length} agent operations · active",
            1 => activeLabels[0],
            _ => runningTool is null
                ? Phase(latest?.Kind, isRetry)
                : $"{ToolLabel(runningTool.Tool)} · running",
        };
        string elapsedText = Duration(elapsed);
        string ageText = updateAge < TimeSpan.FromSeconds(5)
            ? "just now"
            : $"{Duration(updateAge)} ago";
        string operation = goals.WorkflowOperationName ?? "Agent workflow";

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
        if (currentSession.Length > 0)
        {
            details += "\n\nRecent session activity\n" + string.Join('\n', currentSession
                .TakeLast(MaximumTimelineItems)
                .Select(item => $"{item.StartedAt:HH:mm:ss} · {RoleLabel(item.Role)} · " +
                    SessionLabel(item, isRetry)));
        }
        string accessibleName = $"Agent activity: {phase}. Elapsed {elapsedText}. " +
            $"Last observable update {ageText}. Activate for details.";

        return new(
            IsVisible: true,
            $"{phase} · {elapsedText}",
            accessibleName,
            details);
    }

    private static string[] ActiveLabels(
        IReadOnlyList<AgentActivityView> session,
        IReadOnlyList<ToolEvidenceView> tools,
        bool isRetry)
    {
        List<string> labels = session.Select(item => SessionLabel(item, isRetry)).ToList();
        foreach (ToolEvidenceView tool in tools.Where(item =>
                     item.State is ToolEvidenceState.Running))
        {
            string label = $"{ToolLabel(tool.Tool)} · running";
            if (!labels.Contains(label, StringComparer.Ordinal))
            {
                labels.Add(label);
            }
        }
        return labels.ToArray();
    }

    private static string SessionLabel(AgentActivityView activity, bool isRetry) =>
        activity.Kind switch
    {
        AgentActivityKind.ProviderRequest => activity.Phase switch
        {
            AgentActivityPhase.WaitingForResponse =>
                $"{RoleLabel(activity.Role, isRetry)} · contacting model",
            AgentActivityPhase.ReceivingResponse =>
                $"{RoleLabel(activity.Role, isRetry)} · receiving response",
            AgentActivityPhase.Completed =>
                $"{RoleLabel(activity.Role, isRetry)} · model response completed",
            AgentActivityPhase.Failed =>
                $"{RoleLabel(activity.Role, isRetry)} · model request failed",
            AgentActivityPhase.Cancelled =>
                $"{RoleLabel(activity.Role, isRetry)} · model request cancelled",
            _ => $"{RoleLabel(activity.Role, isRetry)} · model active",
        },
        AgentActivityKind.ToolInvocation =>
            $"{OperationLabel(activity.Operation)} · {PhaseLabel(activity.Phase)}",
        _ => "Agent operation · active",
    };

    private static bool IsActive(AgentActivityPhase phase) => phase is
        AgentActivityPhase.WaitingForResponse or
        AgentActivityPhase.ReceivingResponse or
        AgentActivityPhase.Running;

    private static string PhaseLabel(AgentActivityPhase phase) => phase switch
    {
        AgentActivityPhase.WaitingForResponse => "waiting",
        AgentActivityPhase.ReceivingResponse => "receiving response",
        AgentActivityPhase.Running => "running",
        AgentActivityPhase.Completed => "completed",
        AgentActivityPhase.Failed => "failed",
        AgentActivityPhase.Cancelled => "cancelled",
        _ => "unknown",
    };

    private static string RoleLabel(AgentRole role, bool isRetry = false) => role switch
    {
        AgentRole.Lead => "Lead",
        AgentRole.Implementer when isRetry => "Implementer retry",
        AgentRole.Implementer => "Implementer",
        AgentRole.Reviewer => "Reviewer",
        _ => "Agent",
    };

    private static string OperationLabel(AgentActivityOperation operation) =>
        operation.Value switch
        {
            "read_file" => "Read file",
            "read_file_range" => "Read file range",
            "list_workspace_tree" => "List workspace tree",
            "search_text" => "Search text",
            "search_regex" => "Search regex",
            "inspect_git" => "Inspect Git",
            "inspect_dotnet" => "Inspect .NET",
            "inspect_project_graph" => "Inspect project graph",
            "inspect_open_documents" => "Inspect open documents",
            "search_semantic_context" => "Search semantic context",
            "inspect_code_problems" => "Inspect code problems",
            "inspect_project_problems" => "Inspect project problems",
            "get_symbol_info" => "Get symbol info",
            "find_symbol_definition" => "Find symbol definition",
            "find_symbol_references" => "Find symbol references",
            "find_symbol_implementations" => "Find symbol implementations",
            "inspect_code" => "Inspect code",
            "search_symbols" => "Search symbols",
            "analyze_calls" => "Analyze calls",
            "get_type_hierarchy" => "Get type hierarchy",
            "find_associated_tests" => "Find associated tests",
            "lookup_documentation" => "Look up documentation",
            "inspect_dependencies" => "Inspect dependencies",
            "validate_package_candidate" => "Validate package",
            "preview_sbom" => "Preview SBOM",
            "preview_package_change" => "Preview package change",
            "apply_file_edit" => "File edit",
            "preview_symbol_rename" => "Preview rename",
            "apply_symbol_rename" => "Rename",
            "find_missing_imports" => "Find missing imports",
            "find_code_actions" => "Find code actions",
            "preview_document_transformation" => "Preview code transformation",
            "apply_document_transformation" => "Code transformation",
            "dotnet_build" => "Build",
            "dotnet_test" => "Test",
            "list_tool_evidence" => "Read tool evidence",
            "request_visual_capture" => "Request visual capture",
            "inspect_visual_capture" => "Inspect visual capture",
            "discover_toolsets" => "Discover toolsets",
            "request_toolset" => "Request toolset",
            "post_edit_quality_check" => "Check changed-set quality",
            _ when operation.Value.StartsWith("mcp_", StringComparison.Ordinal) => "MCP tool",
            _ => "Typed tool",
        };

    private static string Phase(GoalWorkflowCheckpointKind? kind, bool isRetry) => kind switch
    {
        GoalWorkflowCheckpointKind.LeadCallStarted => "Lead · waiting for model",
        GoalWorkflowCheckpointKind.ImplementerCallStarted when isRetry =>
            "Implementer retry · waiting for model",
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
    private readonly IAgentActivityReader activityReader;
    private readonly Button button = new();
    private readonly TextBlock details = new()
    {
        MaxWidth = 520,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button openGoal = new() { Content = "Open goal" };
    private readonly Button openEvidence = new() { Content = "Open evidence" };
    private readonly Button cancel = new() { Content = "Cancel workflow" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GoalManagementState goals = GoalManagementState.Initial;
    private ToolEvidenceSnapshot? evidence;
    private CancellationTokenSource? evidenceRefresh;
    private int refreshInProgress;
    private bool disposed;

    internal AgentActivityStatusControl(
        IToolEvidenceService evidenceService,
        IAgentActivityReader activityReader)
    {
        this.evidenceService = evidenceService;
        this.activityReader = activityReader;
        button.Classes.Add("command");
        button.IsVisible = false;
        button.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(8),
                Children =
                {
                    details,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { openGoal, openEvidence, cancel },
                    },
                },
            },
        };
        AutomationProperties.SetName(button, "Agent activity status");
        AutomationProperties.SetName(details, "Current agent activity details");
        AutomationProperties.SetName(openGoal, "Open active goal conversation");
        AutomationProperties.SetName(openEvidence, "Open active goal workflow evidence");
        AutomationProperties.SetName(cancel, "Cancel active agent workflow");
        openGoal.Click += (_, _) => GoalRequested?.Invoke();
        openEvidence.Click += (_, _) => EvidenceRequested?.Invoke();
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        activityReader.Changed += OnActivityChanged;
        timer.Tick += OnTimerTick;
    }

    internal event Action? CancelRequested;
    internal event Action? GoalRequested;
    internal event Action? EvidenceRequested;

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
        disposed = true;
        activityReader.Changed -= OnActivityChanged;
        timer.Stop();
        evidenceRefresh?.Cancel();
        evidenceRefresh?.Dispose();
    }

    private void Render(DateTimeOffset now)
    {
        AgentActivityStatusView view = AgentActivityStatusProjector.Project(
            goals,
            now,
            evidence,
            activityReader.GetSnapshot());
        button.IsVisible = view.IsVisible;
        button.Content = view.CompactText;
        details.Text = view.Details;
        AutomationProperties.SetName(button, view.AccessibleName);
        ToolTip.SetTip(button, view.AccessibleName);
        cancel.IsEnabled = view.IsVisible;
        openGoal.IsEnabled = view.IsVisible && goals.SelectedGoalId is not null;
        openEvidence.IsEnabled = openGoal.IsEnabled && goals.Workflow?.Evidence.Count > 0;
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

    private void OnActivityChanged()
    {
        if (!disposed)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed)
                {
                    Render(TimeProvider.System.GetUtcNow());
                }
            });
        }
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
