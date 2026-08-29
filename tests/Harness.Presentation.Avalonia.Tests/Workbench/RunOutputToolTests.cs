using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Model.Controls;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Mutations;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
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
}
