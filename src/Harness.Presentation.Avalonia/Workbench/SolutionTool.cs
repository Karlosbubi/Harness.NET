using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class SolutionTool
{
    private readonly WorkbenchToolContext context;
    private readonly TreeView tree = new();
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private bool busy;
    private int contextVersion;
    private string? workspaceId;
    private string? selectedGoalId;

    internal SolutionTool(WorkbenchToolContext context)
    {
        this.context = context;
        Content = BuildContent();
        RenderUnavailable("Select a workspace to inspect its .NET solution.");
    }

    internal Control Content { get; }
    internal TreeView Tree => tree;
    internal string StatusText => status.Message ?? string.Empty;

    internal void Update(AvaloniaShellState snapshot)
    {
        WorkspaceView? active = snapshot.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        string? nextGoalId = snapshot.Goals.SelectedGoal is { } goal && active is not null &&
                             goal.WorkspaceId == active.Id
            ? goal.Id.Value
            : null;
        if (string.Equals(workspaceId, active?.Id, StringComparison.Ordinal) &&
            string.Equals(selectedGoalId, nextGoalId, StringComparison.Ordinal))
        {
            return;
        }

        contextVersion++;
        workspaceId = active?.Id;
        selectedGoalId = nextGoalId;
        if (active is not { IsTrusted: true })
        {
            RenderUnavailable(active is null
                ? "Select a workspace to inspect its .NET solution."
                : "Trust the workspace to inspect its .NET solution.");
            return;
        }

        status.Message = nextGoalId is null
            ? "Source context: original workspace."
            : "Source context changed; loading the approved goal worktree solution.";
        Dispatcher.UIThread.Post(async () => await RefreshAsync());
    }

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        if (busy)
        {
            return;
        }

        if (active is not { IsTrusted: true })
        {
            RenderUnavailable(active is null
                ? "Select a workspace to inspect its .NET solution."
                : "Trust the workspace to inspect its .NET solution.");
            return;
        }

        int refreshVersion = contextVersion;
        busy = true;
        status.Message = "Loading static .NET solution metadata…";
        status.Severity = StatusSeverity.Information;
        try
        {
            WorkbenchDotNetInspectionResult result = await context.InspectionService.InspectDotNetAsync(
                context.Request(active),
                context.CancellationToken);
            if (refreshVersion != contextVersion)
            {
                return;
            }

            Render(result);
        }
        catch (OperationCanceledException)
        {
            if (refreshVersion == contextVersion)
            {
                RenderUnavailable("Solution metadata loading was cancelled.", StatusSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            if (refreshVersion == contextVersion)
            {
                RenderUnavailable(exception.Message, StatusSeverity.Error);
            }
        }
        finally
        {
            busy = false;
            if (refreshVersion != contextVersion)
            {
                Dispatcher.UIThread.Post(async () => await RefreshAsync());
            }
        }
    }

    private Control BuildContent()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            Margin = new Thickness(8),
            RowSpacing = 6,
        };
        Grid header = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = ".NET solution",
            FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        AccessibleIconButton refresh = new()
        {
            Content = "↻",
            AccessibleName = "Refresh .NET solution metadata",
        };
        refresh.Classes.Add("icon");
        refresh.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        grid.Children.Add(header);

        tree.ItemTemplate = new FuncTreeDataTemplate<SolutionTreeNode>(
            (node, _) => node.Path is null
                ? new TextBlock { Text = node.Label, TextWrapping = TextWrapping.Wrap }
                : FileButton(node),
            node => node.Children);
        AutomationProperties.SetName(tree, ".NET solution project tree");
        Grid.SetRow(tree, 1);
        grid.Children.Add(tree);

        Grid.SetRow(status, 2);
        grid.Children.Add(status);
        return grid;
    }

    private Button FileButton(SolutionTreeNode node)
    {
        Button button = new()
        {
            Content = node.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Classes.Add("tree-file");
        AutomationProperties.SetName(button, $"Open {node.Path!.Value}");
        button.Click += async (_, _) =>
            await context.OpenFileAsync(node.Path.Value, context.SelectedGoalId());
        return button;
    }

    private void Render(WorkbenchDotNetInspectionResult result)
    {
        if (result.DotNet.Error is not null)
        {
            RenderUnavailable(result.DotNet.Error, StatusSeverity.Error);
            return;
        }

        List<SolutionTreeNode> children = [];
        if (result.DotNet.SdkPolicy is { } sdk)
        {
            children.Add(new(
                $"SDK policy · {sdk.Version ?? "unversioned"} · {sdk.RollForward ?? "default roll-forward"}",
                null,
                []));
        }

        children.AddRange(result.DotNet.Projects.Select(ProjectNode));
        tree.ItemsSource = new[]
        {
            new SolutionTreeNode(
                result.DotNet.EntryPoint,
                new(result.DotNet.EntryPoint),
                children),
        };
        status.Message = $"{result.DotNet.Projects.Count} project(s) · {result.Context.Description}" +
                         (result.DotNet.IsTruncated ? " · bounded result" : string.Empty);
        status.Severity = result.DotNet.IsTruncated
            ? StatusSeverity.Warning
            : StatusSeverity.Success;
    }

    private static SolutionTreeNode ProjectNode(DotNetProjectView project)
    {
        List<SolutionTreeNode> children = [];
        children.Add(new(
            project.TargetFrameworks.Count == 0
                ? "Target frameworks · not declared"
                : "Target frameworks",
            null,
            project.TargetFrameworks.Select(framework =>
                new SolutionTreeNode(framework, null, [])).ToArray()));
        children.Add(new(
            "Project metadata",
            null,
            [
                new($"SDK · {project.Sdk ?? "not declared"}", null, []),
                new($"Language · {project.LanguageVersion ?? "default"}", null, []),
                new($"Nullable · {project.Nullable ?? "default"}", null, []),
            ]));
        children.Add(new(
            project.References.Count == 0 ? "Dependencies · none declared" : "Dependencies",
            null,
            project.References.Select(reference => new SolutionTreeNode(
                $"{reference.Kind} · {reference.Identity}" +
                (reference.Version is null ? string.Empty : $" · {reference.Version}"),
                null,
                [])).ToArray()));
        return new(
            project.Path,
            new WorkbenchDocumentPath(project.Path),
            children);
    }

    private void RenderUnavailable(
        string message,
        StatusSeverity severity = StatusSeverity.Information)
    {
        tree.ItemsSource = Array.Empty<SolutionTreeNode>();
        status.Message = message;
        status.Severity = severity;
    }

    internal sealed record SolutionTreeNode(
        string Label,
        WorkbenchDocumentPath? Path,
        IReadOnlyList<SolutionTreeNode> Children);
}
