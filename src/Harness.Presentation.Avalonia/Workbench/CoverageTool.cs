using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Coverage;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class CoverageTool
{
    private const int MaximumVisibleUncoveredLines = 2_000;
    private readonly WorkbenchToolContext context;
    private readonly IDeveloperCoverageService? coverageService;
    private readonly Func<DeveloperCoverageLine, GoalId?, ValueTask> navigate;
    private readonly TextBox reportPath = new()
    {
        PlaceholderText = "Workspace-relative Cobertura XML path",
    };
    private readonly Button import = new() { Content = "Import", IsEnabled = false };
    private readonly TreeView tree = new();
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private bool busy;
    private string? workspaceId;
    private string? goalId;

    internal CoverageTool(
        WorkbenchToolContext context,
        IDeveloperCoverageService? coverageService,
        Func<DeveloperCoverageLine, GoalId?, ValueTask> navigate)
    {
        this.context = context;
        this.coverageService = coverageService;
        this.navigate = navigate;
        Content = BuildContent();
        RenderUnavailable(coverageService is null
            ? "Coverage import is unavailable."
            : "Select a trusted workspace, then choose a Cobertura report to import.");
    }

    internal Control Content { get; }
    internal TextBox ReportPath => reportPath;
    internal Button Import => import;
    internal TreeView Tree => tree;
    internal string StatusText => status.Message ?? string.Empty;

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
            ? "Coverage source context changed. Refresh or import an exact Cobertura report."
            : "Select a trusted workspace, then choose a Cobertura report to import.");
    }

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? workspace = context.ActiveWorkspace();
        if (!CanOperate(workspace)) return;
        busy = true;
        UpdateEnabled(workspace);
        status.Message = "Loading the latest coverage for the exact source context…";
        status.Severity = StatusSeverity.Information;
        try
        {
            DeveloperCoverageResult result = await coverageService!.GetLatestAsync(
                context.Request(workspace!), context.CancellationToken);
            if (result.Error is not null)
            {
                RenderUnavailable(result.Error, StatusSeverity.Error);
                return;
            }
            if (result.Coverage is null)
            {
                RenderUnavailable("No coverage has been imported for this exact source context.");
                return;
            }
            Render(result.Coverage);
        }
        catch (OperationCanceledException)
        {
            RenderUnavailable("Loading coverage was cancelled.", StatusSeverity.Warning);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or IOException)
        {
            RenderUnavailable(exception.Message, StatusSeverity.Error);
        }
        finally
        {
            busy = false;
            UpdateEnabled(workspace);
        }
    }

    internal async ValueTask ImportAsync()
    {
        WorkspaceView? workspace = context.ActiveWorkspace();
        if (!CanOperate(workspace)) return;
        string path = reportPath.Text?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            status.Message = "Enter a workspace-relative Cobertura XML path.";
            status.Severity = StatusSeverity.Warning;
            return;
        }
        busy = true;
        UpdateEnabled(workspace);
        status.Message = "Reading and confining the selected Cobertura report…";
        status.Severity = StatusSeverity.Information;
        try
        {
            DeveloperCoverageResult result = await coverageService!.ImportAsync(new(
                context.Request(workspace!), new(path)), context.CancellationToken);
            if (result.Coverage is null)
            {
                RenderUnavailable(result.Error ?? "Coverage import failed.", StatusSeverity.Error);
                return;
            }
            Render(result.Coverage);
        }
        catch (OperationCanceledException)
        {
            RenderUnavailable("Coverage import was cancelled.", StatusSeverity.Warning);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or IOException or
                UnauthorizedAccessException)
        {
            RenderUnavailable(exception.Message, StatusSeverity.Error);
        }
        finally
        {
            busy = false;
            UpdateEnabled(workspace);
        }
    }

    private Control BuildContent()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,Auto,Auto,*"),
            Margin = new(8),
            RowSpacing = 6,
        };
        grid.Children.Add(new TextBlock
        {
            Text = "Import an explicit Cobertura report. Harness maps only exact source files " +
                   "inside the active workspace; uncovered lines are evidence, not defects.",
            TextWrapping = TextWrapping.Wrap,
        });

        Grid actions = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6 };
        AutomationProperties.SetName(reportPath, "Coverage report path");
        actions.Children.Add(reportPath);
        AutomationProperties.SetName(import, "Import Cobertura coverage");
        import.Click += async (_, _) => await ImportAsync();
        Grid.SetColumn(import, 1);
        actions.Children.Add(import);
        Button refresh = new() { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Refresh coverage history");
        refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(refresh, 2);
        actions.Children.Add(refresh);
        Grid.SetRow(actions, 1);
        grid.Children.Add(actions);

        AutomationProperties.SetName(status, "Coverage status and provenance");
        Grid.SetRow(status, 2);
        grid.Children.Add(status);

        tree.ItemTemplate = new FuncTreeDataTemplate<CoverageTreeNode>(
            (node, _) => CreateNodeControl(node), node => node.Children);
        AutomationProperties.SetName(tree, "Coverage source hierarchy");
        Grid.SetRow(tree, 3);
        grid.Children.Add(tree);
        return grid;
    }

    internal Control CreateNodeControl(CoverageTreeNode node)
    {
        Button open = new()
        {
            Content = node.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(open, node.Line is null
            ? $"Open coverage source {node.Path.Value}"
            : $"Open uncovered line {node.Line.Line.Value} in {node.Path.Value}");
        open.Click += async (_, _) => await NavigateAsync(node);
        return open;
    }

    internal ValueTask NavigateAsync(CoverageTreeNode node) => navigate(
        node.Line ?? new(node.Path, new(1), new(0)), context.SelectedGoalId());

    private bool CanOperate(WorkspaceView? workspace)
    {
        if (busy) return false;
        if (coverageService is not null && workspace is { IsTrusted: true }) return true;
        RenderUnavailable(coverageService is null
            ? "Coverage import is unavailable."
            : "Select a trusted workspace before reading coverage.", StatusSeverity.Warning);
        return false;
    }

    private void Render(DeveloperCoverageView coverage)
    {
        reportPath.Text = coverage.ReportPath.Value;
        int visibleUncovered = 0;
        CoverageTreeNode[] files = coverage.Lines
            .GroupBy(line => line.Path.Value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                DeveloperCoverageLine[] lines = group.OrderBy(line => line.Line.Value).ToArray();
                int covered = lines.Count(line => line.Hits.Value > 0);
                CoverageTreeNode[] uncovered = lines
                    .Where(line => line.Hits.Value == 0 &&
                                   visibleUncovered++ < MaximumVisibleUncoveredLines)
                    .Select(line => new CoverageTreeNode(
                        $"Line {line.Line.Value:N0} · uncovered",
                        line.Path,
                        line,
                        []))
                    .ToArray();
                decimal percentage = lines.Length == 0 ? 0 : covered * 100m / lines.Length;
                return new CoverageTreeNode(
                    $"{group.Key} · {covered:N0}/{lines.Length:N0} lines ({percentage:0.#}%)",
                    lines[0].Path,
                    Line: null,
                    uncovered);
            })
            .ToArray();
        tree.ItemsSource = files;
        int total = coverage.Lines.Length;
        int hit = coverage.Lines.Count(line => line.Hits.Value > 0);
        int uncoveredCount = total - hit;
        string version = coverage.ProducerVersion.Value == "unknown"
            ? string.Empty
            : $" {coverage.ProducerVersion.Value}";
        status.Message = $"{hit:N0}/{total:N0} instrumented lines covered across " +
                         $"{files.Length:N0} source file(s) · {coverage.Format} · " +
                         $"{coverage.Producer.Value}{version} · " +
                         $"{coverage.SourceDescription.Value}" +
                         (coverage.GeneratedAt is null
                             ? string.Empty
                             : $" · generated {coverage.GeneratedAt.Value.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC") +
                         " · imported " +
                         $"{coverage.ImportedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC · report SHA-256 " +
                         $"{coverage.ReportHash.Value[..Math.Min(12, coverage.ReportHash.Value.Length)]}" +
                         (coverage.UnmappedFileCount > 0
                             ? $" · {coverage.UnmappedFileCount:N0} unmapped file(s)"
                             : string.Empty) +
                         (coverage.IsTruncated ? " · bounded import" : string.Empty) +
                         (uncoveredCount > MaximumVisibleUncoveredLines
                             ? $" · showing first {MaximumVisibleUncoveredLines:N0} uncovered lines"
                             : string.Empty) + ".";
        status.Severity = coverage.IsTruncated || coverage.UnmappedFileCount > 0 ||
                          uncoveredCount > MaximumVisibleUncoveredLines
            ? StatusSeverity.Warning
            : StatusSeverity.Success;
        UpdateEnabled(context.ActiveWorkspace());
    }

    private void RenderUnavailable(
        string message,
        StatusSeverity severity = StatusSeverity.Information)
    {
        tree.ItemsSource = Array.Empty<CoverageTreeNode>();
        status.Message = message;
        status.Severity = severity;
        UpdateEnabled(context.ActiveWorkspace());
    }

    private void UpdateEnabled(WorkspaceView? workspace) =>
        import.IsEnabled = !busy && coverageService is not null && workspace is { IsTrusted: true };

    internal sealed record CoverageTreeNode(
        string Label,
        DeveloperCoverageSourcePath Path,
        DeveloperCoverageLine? Line,
        IReadOnlyList<CoverageTreeNode> Children);
}
