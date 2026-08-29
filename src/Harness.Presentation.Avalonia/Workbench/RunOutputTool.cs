using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class RunOutputTool
{
    private readonly WorkbenchToolContext context;
    private readonly IRunOutputService runOutputService;
    private readonly IDeveloperProjectExecutionService? developerExecutionService;
    private readonly ListBox outputs = new();
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button cancel = new() { Content = "Stop", IsEnabled = false };
    private readonly TextEditor details = CodeEditorView.Create(
        string.Empty,
        isReadOnly: true,
        wordWrap: false,
        showLineNumbers: false,
        path: "run-output.txt");
    private string? fingerprint;
    private bool busy;

    internal RunOutputTool(
        WorkbenchToolContext context,
        IRunOutputService runOutputService,
        IDeveloperProjectExecutionService? developerExecutionService)
    {
        this.context = context;
        this.runOutputService = runOutputService;
        this.developerExecutionService = developerExecutionService;
        Content = BuildContent();
    }

    internal Control Content { get; }

    internal void Update(AvaloniaShellState snapshot, GoalId? selectedGoalId)
    {
        string nextFingerprint = $"{selectedGoalId?.Value}|{snapshot.Goals.Workflow?.State}|" +
                                 $"{snapshot.Goals.Workflow?.Activities.Count ?? 0}|" +
                                 snapshot.Goals.IsWorkflowRunning;
        if (string.Equals(fingerprint, nextFingerprint, StringComparison.Ordinal)) return;
        fingerprint = nextFingerprint;
        Dispatcher.UIThread.Post(async () => await RefreshAsync());
    }

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? workspace = context.ActiveWorkspace();
        GoalView? goal = context.State().Goals.SelectedGoal;
        if (busy) return;
        if (workspace is null || !workspace.IsTrusted)
        {
            outputs.ItemsSource = Array.Empty<RunOutputChoice>();
            details.Text = string.Empty;
            status.Message = workspace is null
                ? "Open a workspace to inspect project and goal runs."
                : "Trust the workspace before inspecting run output.";
            return;
        }

        busy = true;
        status.Message = "Loading project and goal runs…";
        try
        {
            DeveloperExecutionListResult developer = developerExecutionService is null
                ? new([], false, null, null)
                : await developerExecutionService.ListAsync(
                    context.Request(workspace), context.CancellationToken);
            RunOutputSnapshot? goalRuns = goal is null
                ? null
                : await runOutputService.ListAsync(goal.Id, context.CancellationToken);
            if (developer.Error is not null || goalRuns?.Error is not null)
            {
                outputs.ItemsSource = Array.Empty<RunOutputChoice>();
                details.Text = string.Empty;
                status.Message = developer.Error ?? goalRuns?.Error ?? "Run output unavailable.";
                return;
            }

            RunOutputChoice[] choices = developer.Executions
                .Select(item => (RunOutputChoice)new DeveloperRunChoice(item))
                .Concat(goalRuns?.Items.Select(item => (RunOutputChoice)new GoalRunChoice(item)) ?? [])
                .OrderByDescending(item => item.StartedAt)
                .ToArray();
            outputs.ItemsSource = choices;
            status.Message = choices.Length == 0
                ? "No project, Build, Test, or Restore runs are recorded for this source context."
                : $"{choices.Length} project and goal run(s)." +
                  (developer.IsTruncated || goalRuns?.IsTruncated is true
                      ? " Showing the latest bounded results."
                      : string.Empty);
            outputs.SelectedIndex = choices.Length == 0 ? -1 : 0;
            if (choices.Length == 0) details.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
            status.Message = "Run-output refresh cancelled.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ArgumentException)
        {
            outputs.ItemsSource = Array.Empty<RunOutputChoice>();
            details.Text = string.Empty;
            status.Message = $"Run output unavailable: {exception.Message}";
        }
        finally
        {
            busy = false;
        }
    }

    private Control BuildContent()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,2*"),
            Margin = new(10),
            RowSpacing = 8,
        };
        Grid heading = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        AutomationProperties.SetName(status, "Run output status");
        status.Message = "Open a trusted workspace to inspect project and goal runs.";
        heading.Children.Add(status);
        Button refresh = new() { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Refresh run output");
        refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(refresh, 1);
        heading.Children.Add(refresh);
        AutomationProperties.SetName(cancel, "Stop selected project operation");
        cancel.Click += async (_, _) => await CancelSelectedAsync();
        Grid.SetColumn(cancel, 2);
        heading.Children.Add(cancel);
        grid.Children.Add(heading);
        AutomationProperties.SetName(outputs, "Project and goal runs");
        outputs.SelectionChanged += (_, _) => ShowSelected();
        Grid.SetRow(outputs, 1);
        grid.Children.Add(outputs);
        AutomationProperties.SetName(details, "Selected run output");
        Grid.SetRow(details, 2);
        grid.Children.Add(details);
        return grid;
    }

    private void ShowSelected()
    {
        details.Text = outputs.SelectedItem switch
        {
            GoalRunChoice choice => Format(choice.Output),
            DeveloperRunChoice choice => Format(choice.Output),
            _ => string.Empty,
        };
        cancel.IsEnabled = outputs.SelectedItem is DeveloperRunChoice
        {
            Output.State: DeveloperExecutionState.Running,
        };
    }

    internal async ValueTask CancelSelectedAsync()
    {
        if (developerExecutionService is null ||
            outputs.SelectedItem is not DeveloperRunChoice choice ||
            choice.Output.State is not DeveloperExecutionState.Running) return;
        DeveloperExecutionCancelResult cancelled = await developerExecutionService.CancelAsync(
            choice.Output.Id, context.CancellationToken);
        status.Message = cancelled.CancellationRequested
            ? "Stopping the selected project run…"
            : cancelled.Error ?? "The selected project run could not be stopped.";
    }

    private static string Format(DeveloperExecutionView output)
    {
        List<string> lines =
        [
            $"{output.Operation} · {output.State}",
            $"Project: {output.Project.ProjectPath.Value}",
            $"Framework: {output.Project.TargetFramework?.Value ?? "project default"}",
            $"Configuration: {output.Project.Configuration?.Value ?? "project default"}",
            $"Source: {output.SourceDescription}",
            $"Started: {output.StartedAt:O}",
            $"Completed: {(output.CompletedAt is null ? "not completed" : output.CompletedAt.Value.ToString("O"))}",
            $"Exit code: {(output.ExitCode?.ToString() ?? "not reported")}",
            $"Duration: {output.DurationMilliseconds:N0} ms",
        ];
        if (output.Test is not null)
            lines.Insert(2, $"Test: {output.Test.FullyQualifiedName.Value}");
        if (output.Error is not null) lines.Add($"Operation error: {output.Error}");
        lines.Add(string.Empty);
        if (!output.IsOutputAvailable)
        {
            lines.Add(output.State is DeveloperExecutionState.Running
                ? "Output becomes available when this bounded run completes."
                : "Raw output is no longer available. Harness.NET persists run metadata, not potentially sensitive application output.");
            return string.Join(Environment.NewLine, lines);
        }
        lines.Add(output.IsOutputTruncated ? "Standard output · truncated" : "Standard output");
        lines.Add(output.StandardOutput?.Value ?? string.Empty);
        lines.Add(string.Empty);
        lines.Add(output.IsErrorTruncated ? "Standard error · truncated" : "Standard error");
        lines.Add(output.StandardError?.Value ?? string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(RunOutputView output)
    {
        List<string> lines =
        [
            $"{output.Operation} · {output.State}",
            $"Started: {output.StartedAt:O}",
            $"Completed: {(output.CompletedAt is null ? "not recorded" : output.CompletedAt.Value.ToString("O"))}",
            $"Correlation: {output.CorrelationId.Value}",
        ];
        if (output.Error is not null)
        {
            lines.Add($"Evidence error: {output.Error}");
            return string.Join(Environment.NewLine, lines);
        }
        if (output.Result is not { } result)
        {
            lines.Add(output.State is ToolEvidenceState.Running
                ? "The run is still active; output becomes available with durable completion evidence."
                : "No completed output was recorded for this run.");
            return string.Join(Environment.NewLine, lines);
        }
        lines.Add($"Entry point: {result.EntryPoint}");
        lines.Add($"Exit code: {(result.ExitCode?.ToString() ?? "not reported")}");
        lines.Add($"Duration: {result.DurationMilliseconds:N0} ms");
        lines.Add($"Cancelled: {(result.WasCancelled ? "yes" : "no")}");
        if (result.Error is not null) lines.Add($"Operation error: {result.Error}");
        lines.Add(string.Empty);
        lines.Add(result.IsOutputTruncated ? "Standard output · truncated" : "Standard output");
        lines.Add(result.StandardOutput);
        lines.Add(string.Empty);
        lines.Add(result.IsErrorTruncated ? "Standard error · truncated" : "Standard error");
        lines.Add(result.StandardError);
        return string.Join(Environment.NewLine, lines);
    }

    private abstract record RunOutputChoice(DateTimeOffset StartedAt);

    private sealed record GoalRunChoice(RunOutputView Output) : RunOutputChoice(Output.StartedAt)
    {
        public override string ToString()
        {
            string exit = Output.Result?.ExitCode is { } code ? $" · exit {code}" : string.Empty;
            return $"{Output.Operation} · {Output.State}{exit} · {Output.StartedAt.LocalDateTime:g}";
        }
    }

    private sealed record DeveloperRunChoice(DeveloperExecutionView Output)
        : RunOutputChoice(Output.StartedAt)
    {
        public override string ToString()
        {
            string exit = Output.ExitCode is { } code ? $" · exit {code}" : string.Empty;
            string target = Output.Test?.FullyQualifiedName.Value ??
                            Output.Project.ProjectPath.Value;
            return $"{Output.Operation} {target} · {Output.State}{exit} · " +
                   $"{Output.StartedAt.LocalDateTime:g}";
        }
    }
}
