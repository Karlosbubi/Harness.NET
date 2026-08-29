using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Goals;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class AvaloniaPresentationStoreTests
{
    [Fact]
    public async Task Loads_and_saves_editor_intelligence_preferences()
    {
        EditorIntelligenceSettingsService editor = new();
        using AvaloniaPresentationStore store = CreateStore(
            editorIntelligenceSettingsService: editor);

        await store.LoadAsync(CancellationToken.None);
        await store.SaveEditorIntelligenceSettingsAsync(new(
            false, true, false, true, false, false, true), CancellationToken.None);

        Assert.Equal(new(true, true, true, true, true, true, true), editor.Initial);
        Assert.Equal(new(false, true, false, true, false, false, true),
            store.Current.Settings.EditorIntelligenceSettings?.Preferences);
        Assert.Equal(1, editor.SaveCalls);
    }

    [Fact]
    public async Task Selects_theme_through_business_boundary()
    {
        AppearanceService appearance = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            appearance,
            new WorkspaceService(),
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

        await store.SelectThemeAsync("harness.dark", CancellationToken.None);

        Assert.Equal("harness.dark", appearance.Selected.Value);
        Assert.Equal("harness.dark", store.Current.Appearance?.PreferredThemeId.Value);
    }

    [Fact]
    public async Task Discovers_and_persists_agent_defaults_through_typed_boundary()
    {
        AgentDefaultsService defaults = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            new WorkspaceService(),
            new GoalService(),
            new GoalModelService(),
            defaults,
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1, defaults.DiscoveryCount);
        Assert.Single(store.Current.Settings.AgentDefaults!.Models);

        await store.DiscoverAgentDefaultsAsync(CancellationToken.None);
        GoalModelCandidate candidate = Assert.Single(
            store.Current.Settings.AgentDefaults!.Models);
        await store.UpdateAgentDefaultAsync(AgentRole.Reviewer, candidate,
            AgentReasoningPolicy.ProviderDefault, CancellationToken.None);

        AgentRoleDefault reviewer = store.Current.Settings.AgentDefaults!.Roles
            .Single(item => item.Role is AgentRole.Reviewer);
        Assert.True(reviewer.IsPersisted);
        Assert.Equal(AgentReasoningPolicy.ProviderDefault, reviewer.ReasoningPolicy);
        Assert.Equal("Saved Reviewer defaults.", store.Current.Settings.Status);
    }

}
