using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Dock.Model.Core;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Git_worktree_tool_creates_new_branch_against_exact_repository_and_set_state()
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
            TabControl tabs = Assert.Single(gitTool.GetVisualDescendants().OfType<TabControl>(), item =>
                AutomationProperties.GetName(item) == "Git workbench sections");
            TabItem worktreeTab = Assert.IsType<TabItem>(
                tabs.Items.OfType<TabItem>().ElementAt(3));
            Control worktreePanel = Assert.IsAssignableFrom<Control>(worktreeTab.Content);
            TextBox path = Assert.Single(worktreePanel.GetLogicalDescendants().OfType<TextBox>(), item =>
                AutomationProperties.GetName(item) == "New linked Git worktree absolute path");
            TextBox branch = Assert.Single(worktreePanel.GetLogicalDescendants().OfType<TextBox>(), item =>
                AutomationProperties.GetName(item) == "Linked Git worktree branch name");
            CheckBox createBranch = Assert.Single(worktreePanel.GetLogicalDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Create new local branch for linked Git worktree");
            path.Text = "/work/new-feature";
            branch.Text = "new-feature";
            createBranch.IsChecked = true;

            workbench.CreateGitWorktreeAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.WorktreeCreateCommand!.ExpectedFingerprint.Value);
            Assert.Equal("worktree-fingerprint",
                git.WorktreeCreateCommand.ExpectedWorktreeFingerprint.Value);
            Assert.Equal("/work/new-feature", git.WorktreeCreateCommand.Path.Value);
            Assert.Equal("new-feature", git.WorktreeCreateCommand.NewBranch!.Value);
            Assert.Null(git.WorktreeCreateCommand.ExistingBranch);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_worktree_tool_opens_selected_path_through_workspace_flow()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            string? requestedPath = null;
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), developerGit: new DeveloperGitService(),
                manageWorkspaceAt: path =>
                {
                    requestedPath = path;
                    return Task.CompletedTask;
                });
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TabControl tabs = Assert.Single(gitTool.GetVisualDescendants().OfType<TabControl>(), item =>
                AutomationProperties.GetName(item) == "Git workbench sections");
            TabItem worktreeTab = Assert.IsType<TabItem>(
                tabs.Items.OfType<TabItem>().ElementAt(3));
            Control worktreePanel = Assert.IsAssignableFrom<Control>(worktreeTab.Content);
            ListBox worktrees = Assert.Single(worktreePanel.GetLogicalDescendants().OfType<ListBox>(), item =>
                AutomationProperties.GetName(item) == "Linked Git worktrees");
            worktrees.SelectedIndex = 1;

            workbench.OpenSelectedGitWorktreeAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("/work/feature", requestedPath);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_worktree_remove_requires_exact_preview_and_confirmation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            DocumentPrompt prompt = new();
            prompt.GitWorktreeRemoveDecisions.Enqueue(true);
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TabControl tabs = Assert.Single(gitTool.GetVisualDescendants().OfType<TabControl>(), item =>
                AutomationProperties.GetName(item) == "Git workbench sections");
            TabItem worktreeTab = Assert.IsType<TabItem>(
                tabs.Items.OfType<TabItem>().ElementAt(3));
            Control worktreePanel = Assert.IsAssignableFrom<Control>(worktreeTab.Content);
            ListBox worktrees = Assert.Single(worktreePanel.GetLogicalDescendants().OfType<ListBox>(), item =>
                AutomationProperties.GetName(item) == "Linked Git worktrees");
            worktrees.SelectedIndex = 1;

            workbench.RemoveSelectedGitWorktreeAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.WorktreeRemoveCommand!.ExpectedFingerprint.Value);
            Assert.Equal("worktree-fingerprint",
                git.WorktreeRemoveCommand.ExpectedWorktreeFingerprint.Value);
            Assert.Equal("/work/feature", git.WorktreeRemoveCommand.Path.Value);
            Assert.Same(Assert.Single(prompt.GitWorktreeRemovePreviews), git.AppliedWorktreeRemove);
            Assert.Contains("Removed linked worktree", workbench.GitStatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_stash_tool_creates_and_applies_with_exact_displayed_state()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            TabControl tabs = Assert.Single(gitTool.GetVisualDescendants().OfType<TabControl>(), item =>
                AutomationProperties.GetName(item) == "Git workbench sections");
            TabItem stashTab = Assert.IsType<TabItem>(tabs.Items.OfType<TabItem>().ElementAt(4));
            Control stashPanel = Assert.IsAssignableFrom<Control>(stashTab.Content);
            TextBox message = Assert.Single(stashPanel.GetLogicalDescendants().OfType<TextBox>(), item =>
                AutomationProperties.GetName(item) == "New Git stash message");
            CheckBox include = Assert.Single(stashPanel.GetLogicalDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Include untracked files in new Git stash");
            message.Text = "checkpoint";
            include.IsChecked = true;

            workbench.CreateGitStashAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.StashCreateCommand!.ExpectedFingerprint.Value);
            Assert.Equal("checkpoint", git.StashCreateCommand.Message.Value);
            Assert.True(git.StashCreateCommand.IncludeUntracked);

            workbench.ApplySelectedGitStashAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.StashApplyCommand!.ExpectedFingerprint.Value);
            Assert.Equal(new string('c', 40), git.StashApplyCommand.Stash.Value);
            Assert.Contains("remains available", workbench.GitStatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_stash_delete_requires_exact_preview_and_confirmation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            DocumentPrompt prompt = new();
            prompt.GitStashDropDecisions.Enqueue(true);
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();

            workbench.DropSelectedGitStashAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.StashDropCommand!.ExpectedFingerprint.Value);
            Assert.Equal(new string('c', 40), git.StashDropCommand.Stash.Value);
            Assert.Same(Assert.Single(prompt.GitStashDropPreviews), git.AppliedStashDrop);
            Assert.Contains("Deleted stash", workbench.GitStatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }
}
