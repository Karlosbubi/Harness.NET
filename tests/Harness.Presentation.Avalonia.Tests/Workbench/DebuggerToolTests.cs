using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests.Workbench;

[Collection("Avalonia UI")]
public sealed class DebuggerToolTests
{
    [Fact]
    public async Task Renders_stopped_session_and_drives_typed_inspection_navigation_and_continue()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DebuggerService debugger = new();
            (string Path, int Line, GoalId? Goal)? navigation = null;
            DebuggerTool tool = new(
                debugger,
                (path, line, goal) =>
                {
                    navigation = (path, line, goal);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None);
            Window window = new() { Width = 800, Height = 700, Content = tool.Content };
            window.Show();

            tool.TrackAsync(debugger.Current).AsTask().GetAwaiter().GetResult();

            Assert.Contains("Stopped: breakpoint", tool.StatusText, StringComparison.Ordinal);
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(tool.Threads.ItemsSource));
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(tool.Stack.ItemsSource));
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(tool.Scopes.ItemsSource));
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(tool.Variables.ItemsSource));

            Button open = tool.Content.GetVisualDescendants().OfType<Button>().Single(button =>
                AutomationProperties.GetName(button) == "Open selected managed stack frame");
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(("src/App/Program.cs", 12, new GoalId("goal-1")), navigation);

            Button resume = tool.Content.GetVisualDescendants().OfType<Button>().Single(button =>
                AutomationProperties.GetName(button) == "Continue managed debug session");
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal((DeveloperDebugCommand.Continue, new DeveloperDebugThreadId(7)),
                Assert.Single(debugger.Commands));
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class DebuggerService : IDeveloperDebuggerService
    {
        internal DeveloperDebugSessionView Current { get; private set; } = View();
        internal List<(DeveloperDebugCommand Command, DeveloperDebugThreadId Thread)> Commands
            { get; } = [];

        public ValueTask<DeveloperDebugStartResult> StartAsync(
            DeveloperDebugStartRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperDebugStartResult(Current, null, null));

        public ValueTask<DeveloperDebugSessionResult> GetAsync(
            DeveloperDebugSessionId sessionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperDebugSessionResult(Current, null, null));

        public ValueTask<DeveloperDebugSessionResult> CommandAsync(
            DeveloperDebugSessionId sessionId,
            DeveloperDebugCommand command,
            DeveloperDebugThreadId threadId,
            CancellationToken cancellationToken = default)
        {
            Commands.Add((command, threadId));
            Current = Current with
            {
                State = DeveloperDebugSessionState.Running,
                Status = "Debugger running…",
            };
            return ValueTask.FromResult(new DeveloperDebugSessionResult(Current, null, null));
        }

        public ValueTask<DeveloperDebugSessionResult> StopAsync(
            DeveloperDebugSessionId sessionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperDebugSessionResult(Current, null, null));

        public ValueTask<DeveloperDebugInspectionResult<DeveloperDebugScope>> GetScopesAsync(
            DeveloperDebugSessionId sessionId,
            DeveloperDebugStackFrameId frameId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new DeveloperDebugInspectionResult<DeveloperDebugScope>(
                    [new("Locals", new(11), false)], null, null));

        public ValueTask<DeveloperDebugInspectionResult<DeveloperDebugVariable>> GetVariablesAsync(
            DeveloperDebugSessionId sessionId,
            DeveloperDebugVariablesReference variablesReference,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new DeveloperDebugInspectionResult<DeveloperDebugVariable>(
                    [new(new("value"), new("42"), new("int"), new(0), null, null)],
                    null, null));

        private static DeveloperDebugSessionView View()
        {
            WorkbenchExecutionTarget target = new(
                WorkbenchExecutionTargetKind.ProjectEntryPoint,
                new("src/App/App.csproj"),
                new("net10.0"),
                new("entry"),
                new("src/App/Program.cs"),
                new(new string('a', 64)),
                new(1));
            return new(
                new("debug-1"),
                new WorkspaceId("workspace-1"),
                new GoalId("goal-1"),
                "Approved goal worktree",
                new(new("src/App/App.csproj"), new("net10.0"), null),
                target,
                DeveloperDebugSessionState.Stopped,
                DeveloperDebugStopReason.Breakpoint,
                new(7),
                null,
                DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                null,
                "Stopped: breakpoint.",
                ImmutableArray<DeveloperDebugBreakpoint>.Empty,
                [new(new(7), "Main Thread")],
                [new(new(9), "Program.Main", new("src/App/Program.cs"), new(12), 5)],
                new("debug output"),
                IsOutputTruncated: false);
        }
    }
}
