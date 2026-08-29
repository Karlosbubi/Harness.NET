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
    public async Task Git_tool_surfaces_actionable_mid_goal_conflict_recovery()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            InspectionService inspection = new() { Status = "Conflicted" };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                inspection: inspection);
            Window window = new() { Content = workbench.Control };
            window.Show();

            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Assert.True(workbench.ShowGit());

            Assert.Contains("1 conflict", workbench.GitSummaryText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("block commit approval", workbench.GitStatusText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stage", workbench.GitStatusText,
                StringComparison.OrdinalIgnoreCase);
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Stage selected Git change");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Unstage selected Git change");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Clear Git hunk or line selection");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Preview discard of selected tracked Git file");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Preview deletion of selected untracked Git file");
            Assert.Contains(gitTool.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Compose developer Git commit from staged changes");
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_tool_stages_selected_path_against_displayed_fingerprint()
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
            ListBox changes = Assert.Single(gitTool.GetLogicalDescendants().OfType<ListBox>(), list =>
                AutomationProperties.GetName(list) == "Git working-tree changes");
            changes.SelectedIndex = 0;

            workbench.UpdateSelectedGitIndexAsync(DeveloperGitIndexAction.Stage)
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.Command!.ExpectedFingerprint.Value);
            Assert.Equal("src/App.cs", Assert.Single(git.Command.Paths).Value);
            Assert.Equal(DeveloperGitIndexAction.Stage, git.Command.Action);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_tool_applies_selected_hunk_by_opaque_identity()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DeveloperGitService git = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), inspection: new() { IncludePatchUnit = true }, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();
            Control gitTool = Assert.IsAssignableFrom<Control>(
                Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context);
            ListBox units = Assert.Single(gitTool.GetLogicalDescendants().OfType<ListBox>(), list =>
                AutomationProperties.GetName(list) == "Git hunks and changed lines");
            units.SelectedIndex = 0;

            workbench.UpdateSelectedGitIndexAsync(DeveloperGitIndexAction.Stage)
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal("patch-unit", git.PatchCommand!.PatchUnitId);
            Assert.Equal("git-fingerprint", git.PatchCommand.ExpectedFingerprint.Value);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_tool_requires_exact_destructive_preview_and_confirmation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            InspectionService inspection = new() { IsUnstaged = true };
            DeveloperGitService git = new();
            DocumentPrompt prompt = new();
            prompt.GitDestructiveDecisions.Enqueue(true);
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, inspection: inspection, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();

            workbench.PreviewAndApplyGitDestructiveAsync(
                    DeveloperGitDestructiveAction.DiscardTrackedWorktree)
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.DestructivePreviewCommand!.ExpectedFingerprint.Value);
            Assert.Equal("src/App.cs", Assert.Single(git.DestructivePreviewCommand.Paths).Value);
            DeveloperGitDestructivePreviewView shown = Assert.Single(prompt.GitDestructivePreviews);
            Assert.False(shown.HasGuaranteedRecovery);
            Assert.Same(shown, git.AppliedDestructivePreview);
            Assert.Contains("preserved", workbench.GitStatusText, StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Destructive_git_action_is_blocked_by_unsaved_editor_buffer()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            InspectionService inspection = new() { IsUnstaged = true };
            DeveloperGitService git = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), inspection: inspection, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved editor content";
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();

            workbench.PreviewAndApplyGitDestructiveAsync(
                    DeveloperGitDestructiveAction.DiscardTrackedWorktree)
                .AsTask().GetAwaiter().GetResult();

            Assert.Null(git.DestructivePreviewCommand);
            Assert.Contains("unsaved editor buffer", workbench.GitStatusText,
                StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_tool_previews_and_confirms_exact_developer_commit()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            InspectionService inspection = new()
            {
                IsStaged = true,
                IndexStatus = "ModifiedInIndex",
                WorktreeStatus = "Unaltered",
            };
            DeveloperGitService git = new();
            DocumentPrompt prompt = new();
            prompt.GitCommitDrafts.Enqueue(new(new("Developer message"),
                DeveloperGitCommitAction.Amend, DeveloperGitCommitHookPolicy.BypassHooks));
            prompt.GitCommitDecisions.Enqueue(true);
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), prompt: prompt, inspection: inspection, developerGit: git);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RefreshGitAsync().AsTask().GetAwaiter().GetResult();

            workbench.ComposeAndCommitGitAsync().AsTask().GetAwaiter().GetResult();

            Assert.Equal("git-fingerprint", git.CommitPreviewCommand!.ExpectedFingerprint.Value);
            Assert.Equal(DeveloperGitCommitAction.Amend, git.CommitPreviewCommand.Action);
            Assert.Equal(DeveloperGitCommitHookPolicy.BypassHooks, git.CommitPreviewCommand.HookPolicy);
            Assert.Equal("Developer message", git.CommitPreviewCommand.Message.Value);
            Assert.Same(Assert.Single(prompt.GitCommitPreviews), git.AppliedCommitPreview);
            Assert.Contains("Amended", workbench.GitStatusText, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }
}

