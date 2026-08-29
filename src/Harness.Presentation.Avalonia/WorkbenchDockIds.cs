namespace Harness.Presentation.Avalonia;

internal static class WorkbenchDockIds
{
    internal const string Root = "dock.root";
    internal const string Workbench = "dock.workbench";
    internal const string Center = "dock.center";
    internal const string Documents = "dock.documents";
    internal const string Left = "dock.left";
    internal const string Right = "dock.right";
    internal const string Bottom = "dock.bottom";
    internal const string NavigationTool = "tool.navigation";
    internal const string FilesTool = "tool.files";
    internal const string ContextTool = "tool.context";
    internal const string GitTool = "tool.git";
    internal const string ConversationTool = "tool.conversation";
    internal const string RunOutputTool = "tool.run-output";
    internal const string TerminalTool = "tool.terminal";
    internal const string ProblemsTool = "tool.problems";
    internal const string OverviewDocument = "document.workspace.overview";
    internal const string DiffDocument = "document.git.diff";
    internal const string PlanDocument = "document.goal.plan";
    internal const string EvidenceDocument = "document.goal.evidence";

    internal static IReadOnlySet<string> DurablePaneIds { get; } = new HashSet<string>(
        [
            NavigationTool,
            FilesTool,
            ContextTool,
            GitTool,
            ConversationTool,
            RunOutputTool,
            TerminalTool,
            ProblemsTool,
            OverviewDocument,
        ],
        StringComparer.Ordinal);
}
