using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class TestExplorerTool
{
    private readonly WorkbenchToolContext context;
    private readonly IWorkbenchCodeIntelligenceService codeIntelligence;
    private readonly IDeveloperProjectExecutionService? execution;
    private readonly Func<WorkbenchCodeTestCase, GoalId?, ValueTask> navigate;
    private readonly Action showRunOutput;
    private readonly Func<ValueTask> refreshRunOutput;
    private readonly TreeView tree = new();
    private readonly TextBox filter = new() { PlaceholderText = "Search tests or traits" };
    private readonly ComboBox frameworkFilter = new()
    {
        ItemsSource = FrameworkFilterChoice.All,
        SelectedIndex = 0,
    };
    private readonly ComboBox stateFilter = new()
    {
        ItemsSource = StateFilterChoice.All,
        SelectedIndex = 0,
    };
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private bool busy;
    private string? workspaceId;
    private string? goalId;

    internal TestExplorerTool(
        WorkbenchToolContext context,
        IWorkbenchCodeIntelligenceService codeIntelligence,
        IDeveloperProjectExecutionService? execution,
        Func<WorkbenchCodeTestCase, GoalId?, ValueTask> navigate,
        Action showRunOutput,
        Func<ValueTask> refreshRunOutput)
    {
        this.context = context;
        this.codeIntelligence = codeIntelligence;
        this.execution = execution;
        this.navigate = navigate;
        this.showRunOutput = showRunOutput;
        this.refreshRunOutput = refreshRunOutput;
        Content = BuildContent();
        RenderUnavailable("Select a trusted workspace to discover tests with Roslyn.");
    }

    internal Control Content { get; }
    internal TreeView Tree => tree;
    internal TextBox Filter => filter;
    internal ComboBox FrameworkFilter => frameworkFilter;
    internal ComboBox StateFilter => stateFilter;
    internal string StatusText => status.Message ?? string.Empty;

    internal ValueTask NavigateAsync(WorkbenchCodeTestCase test) =>
        navigate(test, context.SelectedGoalId());

    internal void Update(AvaloniaShellState snapshot)
    {
        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        string? nextGoal = snapshot.Goals.SelectedGoal is { } selected && active is not null &&
                           selected.WorkspaceId == active.Id
            ? selected.Id.Value
            : null;
        if (workspaceId == active?.Id && goalId == nextGoal) return;
        workspaceId = active?.Id;
        goalId = nextGoal;
        RenderUnavailable(active is { IsTrusted: true }
            ? "Test source context changed. Refresh to discover tests with Roslyn."
            : "Select a trusted workspace to discover tests with Roslyn.");
    }

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? workspace = context.ActiveWorkspace();
        if (busy) return;
        if (workspace is not { IsTrusted: true })
        {
            RenderUnavailable("Select a trusted workspace to discover tests with Roslyn.");
            return;
        }

        busy = true;
        bool acceptingLoadProgress = true;
        status.Message = "Loading the exact Roslyn source context for test discovery…";
        status.Severity = StatusSeverity.Information;
        try
        {
            string entryPoint = Path.IsPathRooted(workspace.EntryPoint)
                ? Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint)
                : workspace.EntryPoint;
            WorkbenchCodeSessionView session = await codeIntelligence.StartAsync(new(
                new(workspace.Id),
                context.SelectedGoalId(),
                new(entryPoint)),
                new Progress<WorkbenchCodeLoadProgress>(progress =>
                {
                    if (acceptingLoadProgress) status.Message = progress.Message.Value;
                }),
                context.CancellationToken);
            acceptingLoadProgress = false;
            if (session.SessionId is null || session.State is WorkbenchCodeResultState.Failed)
            {
                RenderUnavailable(session.Issues.FirstOrDefault()?.Message.Value ??
                    "Roslyn test discovery is unavailable.", StatusSeverity.Error);
                return;
            }
            WorkbenchCodeTestDiscoveryView result = await codeIntelligence.DiscoverTestsAsync(new(
                session.SessionId,
                string.IsNullOrWhiteSpace(filter.Text) ? null : filter.Text.Trim(),
                MaximumResults: 2_000,
                Offset: 0,
                SelectedFramework()), context.CancellationToken);
            if (result.State is WorkbenchCodeResultState.Failed or
                WorkbenchCodeResultState.Stale or WorkbenchCodeResultState.Cancelled)
            {
                RenderUnavailable(result.Issues.FirstOrDefault()?.Message.Value ??
                    "Roslyn test discovery failed.", StatusSeverity.Error);
                return;
            }
            DeveloperExecutionListResult history = execution is null
                ? new([], false, null, null)
                : await execution.ListAsync(context.Request(workspace), context.CancellationToken);
            Render(result, history);
        }
        catch (OperationCanceledException)
        {
            RenderUnavailable("Test discovery was cancelled.", StatusSeverity.Warning);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or IOException or
                UnauthorizedAccessException)
        {
            RenderUnavailable(exception.Message, StatusSeverity.Error);
        }
        finally
        {
            acceptingLoadProgress = false;
            busy = false;
        }
    }

    private Control BuildContent()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,Auto,Auto,Auto,*"),
            Margin = new Thickness(8),
            RowSpacing = 6,
        };
        Grid header = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = "Test Explorer",
            FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Button refresh = new() { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Refresh Test Explorer");
        refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        grid.Children.Add(header);

        AutomationProperties.SetName(filter, "Test Explorer search");
        filter.KeyDown += async (_, args) =>
        {
            if (args.Key is not Key.Enter) return;
            args.Handled = true;
            await RefreshAsync();
        };
        Grid.SetRow(filter, 1);
        grid.Children.Add(filter);

        Grid filters = new() { ColumnDefinitions = new("*,*"), ColumnSpacing = 6 };
        AutomationProperties.SetName(frameworkFilter, "Test framework filter");
        filters.Children.Add(frameworkFilter);
        AutomationProperties.SetName(stateFilter, "Test lifecycle state filter");
        Grid.SetColumn(stateFilter, 1);
        filters.Children.Add(stateFilter);
        Grid.SetRow(filters, 2);
        grid.Children.Add(filters);

        tree.ItemTemplate = new FuncTreeDataTemplate<TestTreeNode>(
            (node, _) => node.Test is null ? new TextBlock
            {
                Text = node.Label,
                TextWrapping = TextWrapping.Wrap,
            } : TestControl(node),
            node => node.Children);
        AutomationProperties.SetName(tree, "Roslyn test hierarchy");
        Grid.SetRow(tree, 4);
        grid.Children.Add(tree);

        AutomationProperties.SetName(status, "Test Explorer status");
        Grid.SetRow(status, 3);
        grid.Children.Add(status);
        return grid;
    }

    private Control TestControl(TestTreeNode node)
    {
        WorkbenchCodeTestCase test = node.Test!;
        string traits = test.Traits.Count == 0
            ? string.Empty
            : " · " + string.Join(", ", test.Traits.Select(item =>
                $"{item.Name.Value}={item.Value.Value}"));
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        Button open = new()
        {
            Content = test.DisplayName.Value +
                      (test.IsParameterized ? " · parameterized" : string.Empty) + traits,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(open, $"Open test {test.FullyQualifiedName.Value}");
        open.Click += async (_, _) => await NavigateAsync(test);
        actions.Children.Add(open);
        if (execution?.Capabilities.CanTest is true)
        {
            if (node.Execution is { State: DeveloperExecutionState.Running } running)
            {
                Button stop = new() { Content = "Stop" };
                AutomationProperties.SetName(stop, $"Stop test {test.FullyQualifiedName.Value}");
                stop.Click += async (_, _) => await CancelTestAsync(running);
                actions.Children.Add(stop);
            }
            else
            {
                Button run = new() { Content = node.Execution is null ? "Run" : "Rerun" };
                AutomationProperties.SetName(run,
                    $"{(node.Execution is null ? "Run" : "Rerun")} test " +
                    test.FullyQualifiedName.Value);
                run.Click += async (_, _) => await StartTestAsync(test);
                actions.Children.Add(run);
            }
        }
        if (node.Execution is not null)
        {
            TextBlock history = new()
            {
                Text = History(node.Execution),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(history,
                $"Test history {test.FullyQualifiedName.Value}: {history.Text}");
            actions.Children.Add(history);
        }
        return actions;
    }

    internal async ValueTask StartTestAsync(WorkbenchCodeTestCase test)
    {
        WorkspaceView? workspace = context.ActiveWorkspace();
        if (execution is null || !execution.Capabilities.CanTest || busy ||
            workspace is not { IsTrusted: true })
        {
            status.Message = "A trusted workspace with developer Test capability is required.";
            status.Severity = StatusSeverity.Warning;
            return;
        }

        busy = true;
        status.Message = $"Starting {test.DisplayName.Value} without Restore…";
        status.Severity = StatusSeverity.Information;
        try
        {
            DeveloperExecutionStartResult result = await execution.StartTestAsync(new(
                context.Request(workspace),
                new(new(test.ProjectPath.Value), TargetFramework: null, Configuration: null),
                new(new(test.Id.Value), new(test.FullyQualifiedName.Value))),
                context.CancellationToken);
            if (result.Execution is null)
            {
                status.Message = result.Error ?? "The selected test could not be started.";
                status.Severity = StatusSeverity.Error;
                return;
            }
            showRunOutput();
            await refreshRunOutput();
            status.Message = $"Started {test.DisplayName.Value}. Follow it in Run output.";
            status.Severity = StatusSeverity.Success;
        }
        catch (OperationCanceledException)
        {
            status.Message = "Starting the selected test was cancelled.";
            status.Severity = StatusSeverity.Warning;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or IOException)
        {
            status.Message = exception.Message;
            status.Severity = StatusSeverity.Error;
        }
        finally
        {
            busy = false;
        }
    }

    internal async ValueTask CancelTestAsync(DeveloperExecutionView running)
    {
        if (execution is null || running.Operation is not DeveloperExecutionOperation.Test ||
            running.State is not DeveloperExecutionState.Running)
            return;
        DeveloperExecutionCancelResult result = await execution.CancelAsync(
            running.Id, context.CancellationToken);
        status.Message = result.CancellationRequested
            ? $"Stopping {running.Test?.FullyQualifiedName.Value ?? "the selected test"}…"
            : result.Error ?? "The selected test could not be stopped.";
        status.Severity = result.CancellationRequested
            ? StatusSeverity.Information
            : StatusSeverity.Error;
        await refreshRunOutput();
    }

    private void Render(
        WorkbenchCodeTestDiscoveryView result,
        DeveloperExecutionListResult history)
    {
        Dictionary<string, DeveloperExecutionView> latest = history.Executions
            .Where(item => item.Operation is DeveloperExecutionOperation.Test &&
                item.Test is not null)
            .GroupBy(item => item.Test!.Id.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        WorkbenchCodeTestCase[] visibleTests = result.Tests
            .Where(item => MatchesState(latest.GetValueOrDefault(item.Id.Value)))
            .ToArray();
        TestTreeNode[] projects = visibleTests
            .GroupBy(item => item.ProjectPath.Value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(project => new TestTreeNode(
                project.Key,
                project.GroupBy(item => ContainingType(item.FullyQualifiedName.Value),
                        StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(type => new TestTreeNode(
                        type.Key,
                        type.OrderBy(item => item.FullyQualifiedName.Value, StringComparer.Ordinal)
                            .Select(item => new TestTreeNode(
                                item.DisplayName.Value,
                                [],
                                item,
                                latest.GetValueOrDefault(item.Id.Value))).ToArray()))
                    .ToArray()))
            .ToArray();
        tree.ItemsSource = projects;
        status.Message = $"{result.Tests.Count:N0} test(s) discovered with Roslyn · " +
                         $"{visibleTests.Length:N0} shown" +
                         (result.IsTruncated ? " · bounded result" : string.Empty) +
                         (history.Error is null ? string.Empty : " · history unavailable") + ".";
        status.Severity = result.IsTruncated || result.State is WorkbenchCodeResultState.Degraded ||
                          history.Error is not null
            ? StatusSeverity.Warning
            : StatusSeverity.Success;
    }

    private static string History(DeveloperExecutionView execution)
    {
        string duration = execution.State is DeveloperExecutionState.Running
            ? "running"
            : $"{execution.DurationMilliseconds:N0} ms";
        string exit = execution.ExitCode is null ? string.Empty : $" · exit {execution.ExitCode}";
        return $"{execution.State} · {duration}{exit}";
    }

    private WorkbenchCodeTestFramework? SelectedFramework() =>
        (frameworkFilter.SelectedItem as FrameworkFilterChoice)?.Framework;

    private bool MatchesState(DeveloperExecutionView? execution)
    {
        TestHistoryFilter selected =
            (stateFilter.SelectedItem as StateFilterChoice)?.Filter ?? TestHistoryFilter.All;
        return selected switch
        {
            TestHistoryFilter.All => true,
            TestHistoryFilter.NotRun => execution is null,
            TestHistoryFilter.Running => execution?.State is DeveloperExecutionState.Running,
            TestHistoryFilter.Succeeded => execution?.State is DeveloperExecutionState.Succeeded,
            TestHistoryFilter.Failed => execution?.State is DeveloperExecutionState.Failed,
            TestHistoryFilter.Cancelled => execution?.State is DeveloperExecutionState.Cancelled,
            TestHistoryFilter.Interrupted => execution?.State is DeveloperExecutionState.Interrupted,
            _ => false,
        };
    }

    private void RenderUnavailable(
        string message,
        StatusSeverity severity = StatusSeverity.Information)
    {
        tree.ItemsSource = Array.Empty<TestTreeNode>();
        status.Message = message;
        status.Severity = severity;
    }

    private static string ContainingType(string fullyQualifiedName)
    {
        int separator = fullyQualifiedName.LastIndexOf('.');
        return separator <= 0 ? "Tests" : fullyQualifiedName[..separator];
    }

    internal sealed record TestTreeNode(
        string Label,
        IReadOnlyList<TestTreeNode> Children,
        WorkbenchCodeTestCase? Test = null,
        DeveloperExecutionView? Execution = null);

    private sealed record FrameworkFilterChoice(
        string Label,
        WorkbenchCodeTestFramework? Framework)
    {
        internal static IReadOnlyList<FrameworkFilterChoice> All { get; } =
        [
            new("All frameworks", null),
            new("xUnit", WorkbenchCodeTestFramework.XUnit),
            new("NUnit", WorkbenchCodeTestFramework.NUnit),
            new("MSTest", WorkbenchCodeTestFramework.MSTest),
        ];

        public override string ToString() => Label;
    }

    private enum TestHistoryFilter
    {
        All,
        NotRun,
        Running,
        Succeeded,
        Failed,
        Cancelled,
        Interrupted,
    }

    private sealed record StateFilterChoice(string Label, TestHistoryFilter Filter)
    {
        internal static IReadOnlyList<StateFilterChoice> All { get; } =
            Enum.GetValues<TestHistoryFilter>()
                .Select(value => new StateFilterChoice(
                    value is TestHistoryFilter.NotRun ? "Not run" : value.ToString(), value))
                .ToArray();

        public override string ToString() => Label;
    }
}
