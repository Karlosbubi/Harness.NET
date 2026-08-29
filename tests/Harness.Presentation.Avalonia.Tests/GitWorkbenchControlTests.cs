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
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
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
    [Fact]
    public async Task Destructive_git_dialog_lists_exact_paths_and_requires_acknowledgement()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            var preview = new DeveloperGitDestructivePreviewView(
                new("preview"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                new("fingerprint"),
                DeveloperGitDestructiveAction.DeleteUntracked,
                [new("scratch.tmp")],
                "Delete one untracked path?",
                "The exact file will be deleted.",
                "Git does not guarantee recovery.",
                HasGuaranteedRecovery: false);
            GitDestructiveConfirmationDialog dialog = new(preview);
            dialog.Show();
            CheckBox acknowledgement = Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>(), box =>
                AutomationProperties.GetName(box) == "Acknowledge destructive Git consequences");
            Button confirm = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button)?.StartsWith("Confirm ", StringComparison.Ordinal) == true);
            ItemsControl paths = Assert.Single(dialog.GetVisualDescendants().OfType<ItemsControl>(), item =>
                AutomationProperties.GetName(item) == "Exact destructive Git paths");
            Assert.False(confirm.IsEnabled);
            Assert.Equal("scratch.tmp", Assert.IsType<string>(Assert.Single(paths.Items)));

            acknowledgement.IsChecked = true;

            Assert.True(confirm.IsEnabled);
            dialog.Close(false);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Developer_commit_dialogs_expose_message_policy_and_exact_diff_accessibly()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            GitCommitComposeDialog compose = new();
            compose.Show();
            Assert.Contains(compose.GetVisualDescendants().OfType<TextBox>(), control =>
                AutomationProperties.GetName(control) == "Developer Git commit message");
            Assert.Contains(compose.GetVisualDescendants().OfType<CheckBox>(), control =>
                AutomationProperties.GetName(control) == "Bypass configured Git commit hooks");
            compose.Close(null);

            var preview = new DeveloperGitCommitPreviewView(
                new("preview"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                new("fingerprint"), DeveloperGitCommitAction.Create,
                DeveloperGitCommitHookPolicy.RunConfiguredHooks, new("Message"),
                "main", new string('a', 40), "Developer", "developer@harness.local",
                [new("src/App.cs")], "diff --git a/src/App.cs b/src/App.cs",
                "A commit will be created.", "It remains in Git history.", false);
            GitCommitConfirmationDialog confirm = new(preview);
            confirm.Show();
            Assert.Contains(confirm.GetVisualDescendants().OfType<TextEditor>(), control =>
                AutomationProperties.GetName(control) == "Exact staged Git diff");
            Assert.Contains(confirm.GetVisualDescendants().OfType<Button>(), control =>
                AutomationProperties.GetName(control) == "Confirm exact developer Git commit");
            confirm.Close(false);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Branch_delete_dialog_shows_exact_tip_and_requires_acknowledgement()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            var preview = new DeveloperGitBranchDeletePreviewView(
                new("preview"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                new("fingerprint"),
                new(new("feature"), new string('b', 40), false, false), true,
                "Delete unmerged feature.", "Recovery is not guaranteed.", false);
            GitBranchDeleteConfirmationDialog dialog = new(preview);
            dialog.Show();
            CheckBox acknowledgement = Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Acknowledge local Git branch deletion consequences");
            Button confirm = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Confirm deletion of exact local Git branch");
            Assert.False(confirm.IsEnabled);
            Assert.Contains(new string('b', 40), dialog.GetVisualDescendants().OfType<TextBlock>()
                .Select(item => item.Text).First(text => text?.Contains("Tip:", StringComparison.Ordinal) == true));
            acknowledgement.IsChecked = true;
            Assert.True(confirm.IsEnabled);
            dialog.Close(false);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Tag_delete_dialog_shows_exact_target_and_requires_acknowledgement()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            var preview = new DeveloperGitTagDeletePreviewView(
                new("preview"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                new("fingerprint"), new(new("v1.0"), new string('c', 40), true, "Release", false),
                "Delete v1.0.", "Recovery is not guaranteed.", false);
            GitTagDeleteConfirmationDialog dialog = new(preview);
            dialog.Show();
            CheckBox acknowledgement = Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Acknowledge local Git tag deletion consequences");
            Button confirm = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Confirm deletion of exact local Git tag");
            Assert.False(confirm.IsEnabled);
            Assert.Contains(new string('c', 40), dialog.GetVisualDescendants().OfType<TextBlock>()
                .Select(item => item.Text).First(text => text?.Contains("Target:", StringComparison.Ordinal) == true));
            acknowledgement.IsChecked = true;
            Assert.True(confirm.IsEnabled);
            dialog.Close(false);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Worktree_remove_dialog_shows_exact_path_head_and_requires_acknowledgement()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            var preview = new DeveloperGitWorktreeRemovePreviewView(
                new("preview"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                new("fingerprint"), new("worktree-fingerprint"),
                new(new("/work/feature"), new("feature"), new string('d', 40), false,
                    false, null, true, false, false, false, new("selected-state")),
                true, "Delete uncommitted content.", "Recovery is not guaranteed.", false);
            GitWorktreeRemoveConfirmationDialog dialog = new(preview);
            dialog.Show();
            CheckBox acknowledgement = Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) ==
                "Acknowledge linked Git worktree removal consequences");
            Button confirm = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Confirm removal of exact linked Git worktree");
            Assert.False(confirm.IsEnabled);
            string exact = dialog.GetVisualDescendants().OfType<TextBlock>()
                .Select(item => item.Text).First(text => text?.Contains("Path:", StringComparison.Ordinal) == true)!;
            Assert.Contains("/work/feature", exact, StringComparison.Ordinal);
            Assert.Contains(new string('d', 40), exact, StringComparison.Ordinal);
            acknowledgement.IsChecked = true;
            Assert.True(confirm.IsEnabled);
            dialog.Close(false);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_history_tool_is_accessible_and_opens_exact_parent_diff()
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
            TabItem historyTab = Assert.IsType<TabItem>(tabs.Items.OfType<TabItem>().ElementAt(5));
            Control historyPanel = Assert.IsAssignableFrom<Control>(historyTab.Content);
            Assert.Contains(historyPanel.GetLogicalDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Load next page of Git history");
            Assert.Contains(historyPanel.GetLogicalDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Show blame for repository path");
            ListBox history = Assert.Single(historyPanel.GetLogicalDescendants().OfType<ListBox>(), item =>
                AutomationProperties.GetName(item) == "Paged Git commit history");
            history.SelectedIndex = -1;
            history.SelectedIndex = 0;
            TextEditor details = Assert.Single(historyPanel.GetLogicalDescendants().OfType<TextEditor>(), item =>
                AutomationProperties.GetName(item) == "Selected Git commit details and parent diffs");

            Assert.Contains("Commit ", details.Text, StringComparison.Ordinal);
            Assert.Contains("empty tree", details.Text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Stash_delete_dialog_shows_exact_commit_and_requires_acknowledgement()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            var stash = new DeveloperGitStashView(
                "stash@{2}", new(new string('e', 40)), new string('a', 40),
                DateTimeOffset.UnixEpoch, "checkpoint", false);
            var preview = new DeveloperGitStashDropPreviewView(
                new("preview"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                new("fingerprint"), stash, "Delete stash.", "Recovery is not guaranteed.", false);
            GitStashDropConfirmationDialog dialog = new(preview);
            dialog.Show();
            CheckBox acknowledgement = Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>(), item =>
                AutomationProperties.GetName(item) == "Acknowledge Git stash deletion consequences");
            Button confirm = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), item =>
                AutomationProperties.GetName(item) == "Confirm deletion of exact Git stash");
            Assert.False(confirm.IsEnabled);
            string exact = dialog.GetVisualDescendants().OfType<TextBlock>()
                .Select(item => item.Text).First(text => text?.Contains("Stash:", StringComparison.Ordinal) == true)!;
            Assert.Contains("stash@{2}", exact, StringComparison.Ordinal);
            Assert.Contains(new string('e', 40), exact, StringComparison.Ordinal);
            acknowledgement.IsChecked = true;
            Assert.True(confirm.IsEnabled);
            dialog.Close(false);
        }, CancellationToken.None);
    }

}
