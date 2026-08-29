using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class AvaloniaPresentationStoreTests
{
    [Theory]
    [InlineData("I am **Gemma 4** 😊</blockquote>", "I am Gemma 4 😊")]
    [InlineData("# Result\n\n> Useful text", "Result\nUseful text")]
    [InlineData("```csharp\nvar value = 1;\n```", "var value = 1;")]
    public void Formats_provider_markdown_without_leaking_markup(
        string source,
        string expected) =>
        Assert.Equal(expected, ConversationContentFormatter.ToReadableText(source));

    [Fact]
    public async Task Loads_and_reduces_streaming_snapshots()
    {
        DashboardService dashboard = new();
        AppearanceService appearance = new();
        using AvaloniaPresentationStore store = new(
            dashboard,
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
        store.SetComposerText("hello");
        await store.SubmitAsync(CancellationToken.None);

        Assert.False(store.Current.IsLoading);
        Assert.False(store.Current.IsStreaming);
        Assert.Equal(string.Empty, store.Current.ComposerText);
        Assert.Equal("Ready after stream", store.Current.Dashboard?.Status);
        Assert.Equal("hello", dashboard.LastInstruction);
    }

    [Fact]
    public async Task First_composer_submission_creates_private_goal_without_calling_provider()
    {
        DashboardService dashboard = new();
        GoalService goals = new();
        using AvaloniaPresentationStore store = new(
            dashboard,
            new AppearanceService(),
            new WorkspaceService(),
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
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);
        WorkspaceView workspace = Assert.Single(store.Current.Workspaces.Registered);
        await store.SetWorkspaceTrustAsync(workspace.Id, true, CancellationToken.None);
        store.SetComposerText(
            "Build a chat-first goal workflow with deterministic authority boundaries and clear feedback.");

        await store.SubmitComposerAsync(CancellationToken.None);

        GoalView goal = Assert.Single(store.Current.Goals.Items);
        Assert.Equal(
            "Build a chat-first goal workflow with deterministic authority…",
            goal.Title);
        Assert.Equal(new ReviewCycleLimit(3), goal.ReviewCycleLimit);
        Assert.Equal(long.MaxValue, goal.RemoteBudget?.Value);
        Assert.Equal(goal.Id, store.Current.Goals.SelectedGoalId);
        Assert.Equal(string.Empty, store.Current.ComposerText);
        Assert.Null(dashboard.LastInstruction);

        ConversationWorkflowCard goalCard = Assert.Single(
            ConversationWorkflowProjector.Project(store.Current.Goals),
            card => card.Kind is ConversationWorkflowCardKind.Goal);
        Assert.Equal(
            [ConversationWorkflowActionKind.ConfigureGoal,
                ConversationWorkflowActionKind.AbortGoal],
            ConversationWorkflowActionProjector.Project(
                    goalCard,
                    store.Current.Goals)
                .Select(action => action.Kind));

        await store.UpdateGoalSettingsAsync(new(
            goal.Id,
            new(5),
            new MicroUsdAmount(2_000_000),
            goal.UpdatedAt), CancellationToken.None);

        Assert.Equal(new ReviewCycleLimit(5), store.Current.Goals.SelectedGoal?.ReviewCycleLimit);
        Assert.Equal(new MicroUsdAmount(2_000_000), store.Current.Goals.SelectedGoal?.RemoteBudget);
        Assert.Null(dashboard.LastInstruction);

        await store.ExtendGoalBudgetAsync(new(
            goal.Id,
            new(2_000_000),
            new(3_000_000),
            new("Explicit recovery increase.")), CancellationToken.None);

        Assert.Equal(new MicroUsdAmount(3_000_000),
            store.Current.Goals.SelectedGoal?.RemoteBudget);
        Assert.Contains("does not retry", store.Current.Goals.Status,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task First_composer_submission_requires_trusted_workspace()
    {
        using AvaloniaPresentationStore store = CreateStore();
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);
        store.SetComposerText("Create a safe goal");

        await store.SubmitComposerAsync(CancellationToken.None);

        Assert.Empty(store.Current.Goals.Items);
        Assert.Equal("Create a safe goal", store.Current.ComposerText);
        Assert.Contains("Trust", store.Current.Error, StringComparison.Ordinal);
    }

}
