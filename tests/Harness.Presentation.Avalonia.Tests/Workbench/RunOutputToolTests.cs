using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Model.Controls;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public void Run_output_formats_typed_adapter_case_results()
    {
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-29T12:00:00Z");
        DeveloperExecutionView execution = new(
            new("test-1"), new("workspace-1"), null, "Original workspace",
            DeveloperExecutionOperation.Test,
            new(new("tests/App.Tests.csproj"), null, null), null,
            DeveloperExecutionState.Failed, started, started.AddMilliseconds(500),
            1, 500, null, null, false, false, false, null, null,
            new(new(new string('a', 64)), new("Demo.Tests"), DeveloperTestScope.Type),
            [
                new(new("Demo.Tests.First"), new("First"),
                    DeveloperTestOutcome.Passed, 100),
                new(new("Demo.Tests.Second"), new("Second"),
                    DeveloperTestOutcome.Failed, 250),
            ],
            AreTestCasesTruncated: true);

        string formatted = RunOutputTool.Format(execution);

        Assert.Contains("Cases: 1 passed · 1 failed · 0 skipped · truncated", formatted,
            StringComparison.Ordinal);
        Assert.Contains("Failed · 250 ms · Second", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_output_tool_renders_typed_developer_build_metadata_and_streams()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperExecutionService output = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), developerExecution: output);
            Window window = new() { Content = workbench.Control };
            window.Show();

            workbench.RefreshRunOutputAsync().AsTask().GetAwaiter().GetResult();

            ITool tool = Find<ITool>(workbench.Root, WorkbenchDockIds.RunOutputTool);
            Control content = Assert.IsAssignableFrom<Control>(tool.Context);
            TextEditor details = Assert.Single(content.GetVisualDescendants().OfType<TextEditor>());
            Assert.Contains("Rebuild · Succeeded", details.Text, StringComparison.Ordinal);
            Assert.Contains("Project: src/App/App.csproj", details.Text, StringComparison.Ordinal);
            Assert.Contains("Configuration: Release", details.Text, StringComparison.Ordinal);
            Assert.Contains("build output", details.Text, StringComparison.Ordinal);
            Assert.Equal("workspace-1", Assert.Single(output.Requests).WorkspaceId.Value);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Run_output_tool_renders_typed_goal_execution_evidence()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RunOutputService output = new()
            {
                Result = new(
                    [new(
                        new("run-1"),
                        new("goal-1"),
                        new("build-1"),
                        DotNetOperation.Build,
                        ToolEvidenceState.Failed,
                        new(
                            "goal-1",
                            new("build-1"),
                            DotNetOperation.Build,
                            "Harness.slnx",
                            1,
                            "compiler output",
                            "CS1002: ; expected",
                            IsOutputTruncated: true,
                            IsErrorTruncated: false,
                            WasCancelled: false,
                            DurationMilliseconds: 725,
                            "process_failed",
                            "Build failed."),
                        now,
                        now.AddMilliseconds(725),
                        Error: null)],
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                runOutput: output);
            Window window = new() { Content = workbench.Control };
            window.Show();

            workbench.RefreshRunOutputAsync().AsTask().GetAwaiter().GetResult();

            ITool tool = Find<ITool>(workbench.Root, WorkbenchDockIds.RunOutputTool);
            Control content = Assert.IsAssignableFrom<Control>(tool.Context);
            ListBox runs = Assert.Single(content.GetVisualDescendants().OfType<ListBox>());
            TextEditor details = Assert.Single(content.GetVisualDescendants().OfType<TextEditor>());
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(runs.ItemsSource));
            Assert.Contains("Build · Failed", details.Text, StringComparison.Ordinal);
            Assert.Contains("Harness.slnx", details.Text, StringComparison.Ordinal);
            Assert.Contains("Standard output · truncated", details.Text, StringComparison.Ordinal);
            Assert.Contains("compiler output", details.Text, StringComparison.Ordinal);
            Assert.Contains("CS1002: ; expected", details.Text, StringComparison.Ordinal);
            Assert.Equal("goal-1", Assert.Single(output.Requests).Value);

            output.Result = new([], false, null, null);
            workbench.RefreshRunOutputAsync().AsTask().GetAwaiter().GetResult();
            TextBlock status = Assert.Single(content.GetVisualDescendants().OfType<TextBlock>(),
                item => AutomationProperties.GetName(item) == "Run output status");
            Assert.Contains("No project, Build, Test, or Restore runs", status.Text,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, details.Text);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class DeveloperExecutionService : IDeveloperProjectExecutionService
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];
        public DeveloperExecutionCapabilities Capabilities { get; } = new(
            true, true, true, false, "Debug unavailable.");

        public ValueTask<DeveloperExecutionStartResult> StartRunAsync(
            DeveloperExecutionStartRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DeveloperExecutionStartResult> StartBuildAsync(
            DeveloperBuildStartRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DeveloperExecutionListResult> ListAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            DateTimeOffset started = DateTimeOffset.Parse("2026-08-29T12:00:00Z");
            return ValueTask.FromResult(new DeveloperExecutionListResult(
                [new(
                    new("build-1"),
                    request.WorkspaceId,
                    request.GoalId,
                    "Original workspace",
                    DeveloperExecutionOperation.Rebuild,
                    new(new("src/App/App.csproj"), null, new("Release")),
                    EntryPoint: null,
                    DeveloperExecutionState.Succeeded,
                    started,
                    started.AddSeconds(2),
                    ExitCode: 0,
                    DurationMilliseconds: 2000,
                    new("build output"),
                    new(string.Empty),
                    IsOutputTruncated: false,
                    IsErrorTruncated: false,
                    IsOutputAvailable: true,
                    ErrorCode: null,
                    Error: null)],
                IsTruncated: false,
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<DeveloperExecutionCancelResult> CancelAsync(
            DeveloperExecutionId executionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeveloperExecutionCancelResult(false, null, null));
    }
}
