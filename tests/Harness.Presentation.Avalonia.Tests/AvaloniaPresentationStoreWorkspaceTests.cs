using Avalonia.Headless;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class AvaloniaPresentationStoreTests
{
    [Fact]
    public async Task Switching_workspaces_restores_each_selected_goal_context()
    {
        WorkspaceView first = new(
            "workspace-1", "/work/first", "First", "/work/first/First.slnx",
            IsTrusted: true, IsActive: true, "main", IsDirty: false);
        WorkspaceView second = new(
            "workspace-2", "/work/second", "Second", "/work/second/Second.slnx",
            IsTrusted: true, IsActive: false, "feature", IsDirty: false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GoalView firstGoal = new(
            new("goal-first"), first.Id, "First goal", "First objective", new(3), null,
            GoalState.Draft, now, now);
        GoalView secondGoal = new(
            new("goal-second"), second.Id, "Second goal", "Second objective", new(3), null,
            GoalState.Draft, now, now);
        MultiWorkspaceService workspaces = new([first, second]);
        MultiGoalService goals = new([firstGoal, secondGoal]);
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            goals,
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        await store.SelectGoalAsync(firstGoal.Id, CancellationToken.None);

        await store.SelectWorkspaceAsync(second.Id, CancellationToken.None);
        Assert.Null(store.Current.Goals.SelectedGoalId);
        await store.SelectGoalAsync(secondGoal.Id, CancellationToken.None);
        await store.SelectWorkspaceAsync(first.Id, CancellationToken.None);

        Assert.Equal(firstGoal.Id, store.Current.Goals.SelectedGoalId);
        Assert.Equal(firstGoal, store.Current.Goals.SelectedGoal);
        await store.SelectWorkspaceAsync(second.Id, CancellationToken.None);
        Assert.Equal(secondGoal.Id, store.Current.Goals.SelectedGoalId);
    }

    [Fact]
    public async Task Registers_selects_and_explicitly_trusts_workspace()
    {
        WorkspaceService workspaces = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            new GoalService(),
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");

        await store.InspectWorkspaceAsync(CancellationToken.None);
        string entryPoint = Assert.Single(store.Current.Workspaces.EntryPoints);
        await store.RegisterWorkspaceAsync(entryPoint, CancellationToken.None);
        WorkspaceView registered = Assert.Single(store.Current.Workspaces.Registered);
        await store.SetWorkspaceTrustAsync(registered.Id, true, CancellationToken.None);

        Assert.True(Assert.Single(store.Current.Workspaces.Registered).IsTrusted);
        Assert.Equal(registered.Id, workspaces.SelectedId);
        Assert.Contains("Trusted", store.Current.Workspaces.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_folder_selection_populates_the_path_and_scans_entry_points()
    {
        WorkspaceService workspaces = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            new GoalService(),
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        FolderPicker picker = new("/work/repository");

        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkspaceDialog dialog = new(store, CancellationToken.None, picker);
            dialog.Show();
            dialog.BrowseRepositoryAsync().GetAwaiter().GetResult();

            Assert.Same(dialog, picker.Owner);
            Assert.Equal("/work/repository", store.Current.Workspaces.RepositoryPath);
            Assert.Single(store.Current.Workspaces.EntryPoints);
            Assert.Contains("Found 1", store.Current.Workspaces.Status, StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Inspects_effective_framework_and_updates_only_private_overlay()
    {
        WorkspaceService workspaces = new();
        FrameworkService framework = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            new GoalService(),
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            framework,
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);

        await store.RefreshFrameworkAsync(CancellationToken.None);
        FrameworkSnapshot initial = Assert.IsType<FrameworkSnapshot>(store.Current.Framework.Snapshot);
        Assert.Contains(initial.Documents, document => document.Source == "AGENTS.md");

        await store.SetPrivateFrameworkOverlayAsync(
            "Prefer immutable records.",
            CancellationToken.None);

        FrameworkDocumentView overlay = Assert.Single(
            store.Current.Framework.Snapshot!.Documents,
            document => document.Layer == "private-workspace");
        Assert.True(overlay.IsPrivate);
        Assert.Equal("Prefer immutable records.", overlay.Content);
        Assert.Equal("workspace-1", framework.LastWorkspaceId);
        Assert.Equal("/work/repository", framework.LastWorkspaceRoot);
    }

}
