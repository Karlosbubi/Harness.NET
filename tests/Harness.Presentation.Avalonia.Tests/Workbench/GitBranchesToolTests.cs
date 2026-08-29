using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Dock.Model.Core;
using Harness.BusinessLogic.Inspection;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Git_branch_tool_creates_and_switches_against_exact_reference_state()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            bool contextRefreshed = false;
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), developerGit: git,
                refreshWorkspaceContext: () => { contextRefreshed = true; return Task.CompletedTask; });
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitBranchesAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TextBox name = Assert.Single(gitTool.GetLogicalDescendants().OfType<TextBox>(), item =>
                AutomationProperties.GetName(item) == "New local Git branch name");
            ListBox branches = Assert.Single(gitTool.GetLogicalDescendants().OfType<ListBox>(), item =>
                AutomationProperties.GetName(item) == "Local Git branches");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Switch to selected local Git branch");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Preview deletion of selected local Git branch");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Force deletion of unmerged local Git branch");
            name.Text = "feature/new";

            workbench.ApplyGitBranchAsync(DeveloperGitBranchAction.Create)
                .AsTask().GetAwaiter().GetResult();
            Assert.Equal(DeveloperGitBranchAction.Create, git.BranchCommand!.Action);
            Assert.Equal("git-fingerprint", git.BranchCommand.ExpectedFingerprint.Value);
            Assert.Equal("feature/new", git.BranchCommand.NewName!.Value);

            branches.SelectedIndex = 0;
            workbench.ApplyGitBranchAsync(DeveloperGitBranchAction.Switch)
                .AsTask().GetAwaiter().GetResult();
            Assert.Equal(DeveloperGitBranchAction.Switch, git.BranchCommand.Action);
            Assert.True(contextRefreshed);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_branch_delete_requires_exact_preview_and_confirmation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            DocumentPrompt prompt = new();
            prompt.GitBranchDeleteDecisions.Enqueue(true);
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitBranchesAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            ListBox branches = Assert.Single(gitTool.GetLogicalDescendants().OfType<ListBox>(), item =>
                AutomationProperties.GetName(item) == "Local Git branches");
            branches.SelectedIndex = 0;

            workbench.DeleteSelectedGitBranchAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.BranchDeleteCommand!.ExpectedFingerprint.Value);
            Assert.Same(Assert.Single(prompt.GitBranchDeletePreviews), git.AppliedBranchDelete);
            Assert.Contains("Deleted local branch", workbench.GitStatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_tag_tool_creates_annotated_tag_against_exact_reference_state()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            WorkbenchDockHost workbench = CreateWorkbench(TrustedShell(), new(), developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TextBox name = Assert.Single(gitTool.GetLogicalDescendants().OfType<TextBox>(), item =>
                AutomationProperties.GetName(item) == "New local Git tag name");
            TextBox message = Assert.Single(gitTool.GetLogicalDescendants().OfType<TextBox>(), item =>
                AutomationProperties.GetName(item) == "Annotated local Git tag message");
            CheckBox annotated = Assert.Single(gitTool.GetLogicalDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Create annotated local Git tag");
            name.Text = "v2.0";
            message.Text = "Release notes";
            annotated.IsChecked = true;

            workbench.CreateGitTagAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.TagCreateCommand!.ExpectedFingerprint.Value);
            Assert.Equal("v2.0", git.TagCreateCommand.Name.Value);
            Assert.True(git.TagCreateCommand.Annotated);
            Assert.Equal("Release notes", git.TagCreateCommand.Message!.Value);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_tag_delete_requires_exact_target_preview_and_confirmation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            DocumentPrompt prompt = new();
            prompt.GitTagDeleteDecisions.Enqueue(true);
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();

            workbench.DeleteSelectedGitTagAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.TagDeleteCommand!.ExpectedFingerprint.Value);
            Assert.Equal("v1.0", git.TagDeleteCommand.Name.Value);
            Assert.Same(Assert.Single(prompt.GitTagDeletePreviews), git.AppliedTagDelete);
            Assert.Contains("Deleted local tag", workbench.GitStatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }
}
