using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using Dock.Model.Controls;
using Harness.BusinessLogic.CodeIntelligence;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Source_buffer_diagnostics_render_in_the_dockable_problems_tool_and_navigate()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Diagnostics = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        new("CS1002"),
                        new("; expected"),
                        new("Compiler"),
                        new("Sample"),
                        snapshot.Path,
                        new(new(0, 9), new(0, 10)),
                        WorkbenchCodeDiagnosticSeverity.Error)],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(),
                new(),
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();

            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            WorkbenchCodeDocumentSnapshot snapshot = Assert.Single(codeIntelligence.Snapshots);
            Assert.Equal(1, snapshot.BufferVersion.Value);
            Assert.Equal("src/App.cs", snapshot.Path.Value);
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(workbench.Problems.ItemsSource));
            Assert.Contains("1 error", workbench.ProblemsStatusText, StringComparison.Ordinal);
            Assert.NotNull(Find<ITool>(workbench.Root, WorkbenchDockIds.ProblemsTool));
            CodeDiagnosticRenderer renderer = Assert.Single(
                workbench.ActiveSourceEditor!.TextArea.TextView.BackgroundRenderers
                    .OfType<CodeDiagnosticRenderer>());
            Assert.Equal(1, renderer.SegmentCount);

            workbench.Problems.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, workbench.ActiveSourceEditor?.TextArea.Caret.Line);
            Assert.Equal(10, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }
}
