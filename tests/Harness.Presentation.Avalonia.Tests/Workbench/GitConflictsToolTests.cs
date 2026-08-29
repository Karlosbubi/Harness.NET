using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Model.Core;
using Harness.BusinessLogic.Documents;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Git_conflict_editor_saves_then_stages_exact_result_as_separate_actions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            CodeIntelligenceService code = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: code, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TabControl tabs = Assert.Single(gitTool.GetVisualDescendants().OfType<TabControl>(), item =>
                AutomationProperties.GetName(item) == "Git workbench sections");
            TabItem conflictsTab = Assert.IsType<TabItem>(tabs.Items.OfType<TabItem>().ElementAt(6));
            Control conflictPanel = Assert.IsAssignableFrom<Control>(conflictsTab.Content);
            TextEditor result = Assert.Single(
                conflictPanel.GetLogicalDescendants().OfType<TextEditor>(), item =>
                    AutomationProperties.GetName(item) == "Editable Git conflict result");
            Assert.Contains("<<<<<<<", result.Text, StringComparison.Ordinal);
            Assert.False(result.IsReadOnly);
            Assert.Contains(code.Snapshots, snapshot =>
                snapshot.Path.Value == "first.cs" &&
                snapshot.Text.Value.Contains("<<<<<<<", StringComparison.Ordinal));
            TextBlock diagnostics = Assert.Single(
                conflictPanel.GetLogicalDescendants().OfType<TextBlock>(), item =>
                    AutomationProperties.GetName(item) == "Git conflict result diagnostics");
            Assert.Contains("Roslyn", diagnostics.Text, StringComparison.Ordinal);
            Assert.Contains(conflictPanel.GetLogicalDescendants().OfType<TextEditor>(), item =>
                AutomationProperties.GetName(item) == "Read-only Git conflict base" && item.IsReadOnly);
            Assert.Contains(conflictPanel.GetLogicalDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) ==
                "Save exact merge result without resolving Git index conflict");
            Assert.Contains(conflictPanel.GetLogicalDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) ==
                "Stage exact saved merge result and resolve selected Git index conflict");

            result.Text = "resolved result\n";
            workbench.SaveGitConflictResultAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("conflict-state", git.ConflictSaveCommand!.ExpectedFingerprint.Value);
            Assert.Equal(new string('d', 64), git.ConflictSaveCommand.ExpectedResultHash.Value);
            Assert.Equal("resolved result\n", git.ConflictSaveCommand.Result);
            workbench.StageSavedGitConflictResultAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal("first.cs", git.ConflictStageCommand!.Path.Value);
            Assert.Equal("conflict-state", git.ConflictStageCommand.ExpectedFingerprint.Value);
            Assert.Equal(new string('d', 64), git.ConflictStageCommand.ExpectedResultHash.Value);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unsaved_merge_result_blocks_exit_until_user_saves_or_discards_it()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, developerGit: new DeveloperGitService());
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TextEditor result = Assert.Single(
                gitTool.GetLogicalDescendants().OfType<TextEditor>(), item =>
                    AutomationProperties.GetName(item) == "Editable Git conflict result");
            result.Text = "unsaved choice\n";
            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);

            Assert.False(workbench.PrepareForShutdownAsync().AsTask().GetAwaiter().GetResult());
            Assert.Equal("unsaved choice\n", result.Text);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            Assert.True(workbench.PrepareForShutdownAsync().AsTask().GetAwaiter().GetResult());
            Assert.Contains("<<<<<<<", result.Text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_conflict_Roslyn_session_reuses_debounces_cancels_and_stops()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            CodeIntelligenceService code = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                shell, new(), codeIntelligence: code, developerGit: new DeveloperGitService());
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();

            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Assert.Single(code.StartRequests);
            Assert.NotEmpty(code.Snapshots);
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TextEditor result = Assert.Single(
                gitTool.GetLogicalDescendants().OfType<TextEditor>(), item =>
                    AutomationProperties.GetName(item) == "Editable Git conflict result");
            string originalResult = result.Text;
            result.Text = "first transient edit";
            result.Text = "final debounced edit";
            Task.Delay(350).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(code.StartRequests);
            Assert.Contains(code.Snapshots, snapshot =>
                snapshot.Text.Value == "final debounced edit");

            result.Text = originalResult;
            Assert.True(workbench.PrepareForShutdownAsync().AsTask().GetAwaiter().GetResult());
            Assert.Contains(code.StoppedSessions, id => id.Value == "session-1");

            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();

            result.Text = "edit cancelled by workspace invalidation";
            workbench.Update(shell with
            {
                Workspaces = WorkspaceManagementState.Initial,
            });
            Task.Delay(350).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(code.Snapshots, snapshot =>
                snapshot.Text.Value == "edit cancelled by workspace invalidation");
            window.Close();
        }, CancellationToken.None);
    }
}
