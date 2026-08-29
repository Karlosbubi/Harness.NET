using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    private static WorkbenchDockHost CreateWorkbench(
        AvaloniaShellState shell,
        LayoutService layouts,
        DocumentService? documents = null,
        DocumentPrompt? prompt = null,
        InspectionService? inspection = null,
        RunOutputService? runOutput = null,
        CodeIntelligenceService? codeIntelligence = null,
        Func<bool, Task>? manageWorkspace = null,
        MutationService? mutationService = null,
        Control? conversation = null,
        IDeveloperGitService? developerGit = null,
        Func<Task>? refreshWorkspaceContext = null,
        Func<string, Task>? manageWorkspaceAt = null,
        IDeveloperProjectExecutionService? developerExecution = null,
        IDeveloperDebuggerService? developerDebugger = null) => new(
        runOutput ?? new RunOutputService(),
        inspection ?? new InspectionService(),
        documents ?? new DocumentService(),
        codeIntelligence ?? new CodeIntelligenceService(),
        layouts,
        prompt ?? new DocumentPrompt(),
        () => shell,
        new TextBlock { Text = "Workspace" },
        conversation ?? new TextBlock { Text = "Conversation" },
        new TextBlock { Text = "Goal context" },
        CancellationToken.None,
        manageWorkspace,
        mutationService,
        null,
        developerExecution,
        developerGit,
        refreshWorkspaceContext,
        manageWorkspaceAt,
        null,
        developerDebugger);

    private static Control ConversationSurface(string text) => new Border
    {
        Child = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Children =
            {
                new ScrollViewer { Content = new TextBlock { Text = text } },
                new TextBox { [Grid.RowProperty] = 1, PlaceholderText = "Message Harness" },
            },
        },
    };

    private static AvaloniaShellState TrustedShell()
    {
        WorkspaceView workspace = new(
            "workspace-1",
            "/work/repository",
            "repository",
            "/work/repository/Harness.slnx",
            IsTrusted: true,
            IsActive: true,
            "main",
            IsDirty: true);
        return AvaloniaShellState.Initial with
        {
            Workspaces = WorkspaceManagementState.Initial with { Registered = [workspace] },
            IsLoading = false,
        };
    }

    private static AvaloniaShellState ApprovedGoalShell()
    {
        AvaloniaShellState shell = TrustedShell();
        GoalView goal = new(
            new("goal-1"),
            "workspace-1",
            "Edit source safely",
            "Change source only in the isolated worktree.",
            new(2),
            RemoteBudget: null,
            GoalState.Approved,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return shell with
        {
            Goals = GoalManagementState.Initial with
            {
                Items = [goal],
                SelectedGoalId = goal.Id,
            },
        };
    }

    private static T Find<T>(IDockable root, string id)
        where T : class, IDockable
    {
        HashSet<IDockable> visited = new(ReferenceEqualityComparer.Instance);
        Stack<IDockable> pending = new();
        pending.Push(root);
        while (pending.TryPop(out IDockable? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.Id == id)
            {
                return Assert.IsAssignableFrom<T>(current);
            }

            if (current is IDock dock)
            {
                foreach (IDockable child in dock.VisibleDockables ?? [])
                {
                    pending.Push(child);
                }
            }

            if (current is IRootDock rootDock)
            {
                foreach (IDockable child in (rootDock.HiddenDockables ?? [])
                             .Concat(rootDock.LeftPinnedDockables ?? [])
                             .Concat(rootDock.RightPinnedDockables ?? [])
                             .Concat(rootDock.TopPinnedDockables ?? [])
                             .Concat(rootDock.BottomPinnedDockables ?? []))
                {
                    pending.Push(child);
                }

                foreach (IDockWindow window in rootDock.Windows ?? [])
                {
                    if (window.Layout is not null)
                    {
                        pending.Push(window.Layout);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Dockable '{id}' was not found.");
    }

    private static IReadOnlyList<ITool> DurableTools(IRootDock root) =>
        WorkbenchDockIds.DurablePaneIds
            .Where(id => id.StartsWith("tool.", StringComparison.Ordinal))
            .Select(id => Find<ITool>(root, id))
        .ToArray();

    private static void RemovePane(JsonNode node, string id)
    {
        if (node is JsonArray array)
        {
            for (int index = array.Count - 1; index >= 0; index--)
            {
                JsonNode? child = array[index];
                if (child is JsonObject candidate &&
                    string.Equals(candidate["Id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    array.RemoveAt(index);
                }
                else if (child is not null)
                {
                    RemovePane(child, id);
                }
            }

            return;
        }

        if (node is JsonObject value)
        {
            foreach (JsonNode child in value.Select(property => property.Value).OfType<JsonNode>())
            {
                RemovePane(child, id);
            }
        }
    }

}
