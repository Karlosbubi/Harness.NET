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

[Collection("Avalonia UI")]
public sealed partial class PresentationControlTests
{
    [Fact]
    public void Workflow_cards_project_durable_and_degraded_states_without_commands()
    {
        AvaloniaShellState shell = ApprovedGoalShell();
        GoalView goal = shell.Goals.SelectedGoal!;
        PlanView plan = new(
            new("plan-1"),
            goal.Id,
            new(2),
            "1. Make the bounded change\n2. Verify it",
            PlanState.Denied,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        IReadOnlyList<ConversationWorkflowCard> cards = ConversationWorkflowProjector.Project(
            shell.Goals with { CurrentPlan = plan },
            "Provider unavailable");

        Assert.Equal(ConversationWorkflowCardState.Approved, cards[0].State);
        Assert.Contains(cards, item =>
            item.Kind is ConversationWorkflowCardKind.Plan &&
            item.State is ConversationWorkflowCardState.Denied);
        Assert.Contains(cards, item => item.State is ConversationWorkflowCardState.Failed);
        Assert.Equal(
            [
                ConversationWorkflowCardState.Loading,
                ConversationWorkflowCardState.Unavailable,
                ConversationWorkflowCardState.Stale,
                ConversationWorkflowCardState.Pending,
                ConversationWorkflowCardState.Active,
                ConversationWorkflowCardState.Paused,
                ConversationWorkflowCardState.Approved,
                ConversationWorkflowCardState.Denied,
                ConversationWorkflowCardState.Failed,
                ConversationWorkflowCardState.Cancelled,
                ConversationWorkflowCardState.Recovered,
                ConversationWorkflowCardState.Completed,
            ],
            Enum.GetValues<ConversationWorkflowCardState>());
    }

    [Fact]
    public void Workflow_actions_expose_only_the_current_plan_decision()
    {
        AvaloniaShellState shell = ApprovedGoalShell();
        GoalView draft = shell.Goals.SelectedGoal! with { State = GoalState.Draft };
        GoalManagementState draftState = shell.Goals with
        {
            Items = [draft],
            CurrentPlan = null,
        };
        ConversationWorkflowCard missingPlan = Assert.Single(
            ConversationWorkflowProjector.Project(draftState),
            card => card.Kind is ConversationWorkflowCardKind.Plan);

        Assert.Equal(
            [
                ConversationWorkflowActionKind.StartPlanning,
                ConversationWorkflowActionKind.WritePlan,
            ],
            ConversationWorkflowActionProjector.Project(missingPlan, draftState)
                .Select(action => action.Kind));

        PlanView pendingPlan = new(
            new("plan-1"),
            draft.Id,
            new(1),
            "1. Implement\n2. Verify",
            PlanState.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        GoalManagementState pendingState = draftState with { CurrentPlan = pendingPlan };
        ConversationWorkflowCard planCard = Assert.Single(
            ConversationWorkflowProjector.Project(pendingState),
            card => card.Kind is ConversationWorkflowCardKind.Plan);

        Assert.Equal(
            [
                ConversationWorkflowActionKind.ApprovePlan,
                ConversationWorkflowActionKind.RequestPlanChanges,
            ],
            ConversationWorkflowActionProjector.Project(planCard, pendingState)
                .Select(action => action.Kind));
    }

    [Fact]
    public void Failed_role_card_exposes_exact_retry_and_abort_recovery()
    {
        AvaloniaShellState shell = ApprovedGoalShell();
        GoalView goal = shell.Goals.SelectedGoal!;
        GoalWorkflowSnapshot workflow = new(
            new("run-retry"),
            goal.Id,
            GoalWorkflowState.NeedsDirection,
            new(0),
            [],
            [new(1, GoalWorkflowCheckpointKind.UserDirectionRequired,
                WorkflowActor.System, new("Provider unavailable; inspect cost evidence."),
                DateTimeOffset.UtcNow)],
            [new(1, new("Recovery notice"), new("Provider unavailable; inspect cost evidence."))],
            CanResume: false,
            RequiresUserDirection: true,
            RetryRole: GoalWorkflowRetryRole.Reviewer);
        GoalManagementState state = shell.Goals with
        {
            Workflow = workflow,
            ModelSelections = [],
        };
        ConversationWorkflowCard runCard = Assert.Single(
            ConversationWorkflowProjector.Project(state),
            card => card.Id == "run.run-retry");
        ConversationWorkflowAction[] actions =
            ConversationWorkflowActionProjector.Project(runCard, state).ToArray();

        Assert.Equal(
            [ConversationWorkflowActionKind.RetryRun,
                ConversationWorkflowActionKind.AbortGoal],
            actions.Select(action => action.Kind));
        Assert.Equal("Retry Reviewer", actions[0].Label);
        Assert.Equal("Current run · Needs your direction", runCard.Title);
        Assert.Equal(ConversationWorkflowCardState.Paused, runCard.State);
        Assert.Contains("Now:", runCard.Summary, StringComparison.Ordinal);
        Assert.Contains("Result so far:", runCard.Summary, StringComparison.Ordinal);
        Assert.Contains("Next: Retry Reviewer as-is", runCard.Summary, StringComparison.Ordinal);
        Assert.Contains("explicitly retrying Reviewer", runCard.Details,
            StringComparison.Ordinal);
        ConversationWorkflowCard direction = Assert.Single(
            ConversationWorkflowProjector.Project(state),
            card => card.Title == "User direction required");
        Assert.Equal(ConversationWorkflowCardState.Paused, direction.State);
        Assert.Contains("Reviewer did not produce a usable decision", direction.Summary,
            StringComparison.Ordinal);
        Assert.Contains("Technical detail: Provider unavailable", direction.Details,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ConversationWorkflowProjector.Project(state),
            card => card.Title == "Recovery notice");

        GoalManagementState remoteState = state with
        {
            ModelSelections =
            [
                new(goal.Id, AgentRole.Reviewer, new("remote"), new("review-model"),
                    ModelAccess.Remote, IsExplicit: true, DateTimeOffset.UtcNow),
            ],
        };
        ConversationWorkflowCard remoteRunCard = Assert.Single(
            ConversationWorkflowProjector.Project(remoteState),
            card => card.Id == "run.run-retry");
        Assert.Equal(
            [ConversationWorkflowActionKind.RetryRun,
                ConversationWorkflowActionKind.AbortGoal],
            ConversationWorkflowActionProjector.Project(remoteRunCard, remoteState)
                .Select(item => item.Kind));

        GoalView cappedGoal = goal with
        {
            RemoteBudget = new(5_000_000),
        };
        GoalManagementState cappedState = remoteState with
        {
            Items = [cappedGoal],
        };
        ConversationWorkflowCard cappedRunCard = Assert.Single(
            ConversationWorkflowProjector.Project(cappedState),
            card => card.Id == "run.run-retry");
        Assert.Equal(
            [ConversationWorkflowActionKind.RetryRun,
                ConversationWorkflowActionKind.ExtendBudget,
                ConversationWorkflowActionKind.AbortGoal],
            ConversationWorkflowActionProjector.Project(cappedRunCard, cappedState)
                .Select(item => item.Kind));

        GoalManagementState correctionState = state with
        {
            Workflow = workflow with
            {
                State = GoalWorkflowState.Running,
                CanResume = true,
                RequiresUserDirection = false,
                RetryRole = null,
            },
            IsWorkflowRunning = false,
        };
        ConversationWorkflowCard correctionCard = Assert.Single(
            ConversationWorkflowProjector.Project(correctionState),
            card => card.Id == "run.run-retry");
        Assert.Equal(
            ConversationWorkflowActionKind.ContinueRun,
            Assert.Single(ConversationWorkflowActionProjector.Project(
                correctionCard, correctionState)).Kind);
    }

    [Fact]
    public void Settings_search_matches_stable_categories_and_related_terms()
    {
        Assert.Equal(14, SettingsCatalog.All.Count);
        Assert.Equal(
            SettingsCategoryId.InboundMcp,
            Assert.Single(SettingsCatalog.Filter("dogfood")).Id);
        Assert.Equal(
            SettingsCategoryId.Appearance,
            Assert.Single(SettingsCatalog.Filter("contrast")).Id);
        Assert.Equal(
            SettingsCategoryId.ModelsAndRoles,
            Assert.Single(SettingsCatalog.Filter("reviewer")).Id);
        Assert.Equal(
            SettingsCategoryId.ModelProviders,
            Assert.Single(SettingsCatalog.Filter("openrouter")).Id);
        Assert.Equal(
            SettingsCategoryId.McpConnections,
            Assert.Single(SettingsCatalog.Filter("stateless")).Id);
        Assert.Equal(
            SettingsCategoryId.AgentTools,
            Assert.Single(SettingsCatalog.Filter("definition")).Id);
        Assert.Equal(
            SettingsCategoryId.VisualVerification,
            Assert.Single(SettingsCatalog.Filter("screenshot")).Id);
        Assert.Equal(
            SettingsCategoryId.DocumentationAndDependencies,
            Assert.Single(SettingsCatalog.Filter("cyclonedx")).Id);
        Assert.Equal(
            SettingsCategoryId.StorageAndRecovery,
            Assert.Single(SettingsCatalog.Filter("backup")).Id);
        Assert.Equal(
            SettingsCategoryId.Editor,
            Assert.Single(SettingsCatalog.Filter("inlay")).Id);
        Assert.Equal(
            SettingsCategoryId.Keybindings,
            Assert.Single(SettingsCatalog.Filter("shortcut")).Id);
        Assert.Empty(SettingsCatalog.Filter("not-a-real-setting"));
        Assert.Equal(11, SettingsCatalog.All.Count(category => category.IsAvailable));
    }

    [Fact]
    public async Task Editor_settings_expose_inlay_and_lazy_code_lens_controls()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore();
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.Editor);
            Dispatcher.UIThread.RunJobs();

            string[] expectedNames =
            [
                "Show Roslyn parameter name inlay hints",
                "Show Roslyn inferred type inlay hints",
                "Show reference CodeLens actions",
                "Show implementation CodeLens actions",
                "Show associated test CodeLens actions",
                "Show project Run CodeLens actions",
                "Show project Debug CodeLens actions",
                "Format C# code on paste",
                "Format C# code on supported typing triggers",
                "Save editor intelligence settings",
            ];
            string?[] actual = window.GetLogicalDescendants().OfType<Control>()
                .Select(AutomationProperties.GetName).ToArray();
            Assert.All(expectedNames, name => Assert.Contains(name, actual));
            Assert.Contains("resolve only when selected", string.Join('\n', window
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Keybinding_settings_expose_conflicts_reset_and_safe_import_export()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            keybindingSettingsService: new KeybindingSettingsService());
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.Keybindings);
            Dispatcher.UIThread.RunJobs();

            TextBox chat = window.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                AutomationProperties.GetName(control) == "Shortcut for Show Chat");
            TextBox quickOpen = window.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                AutomationProperties.GetName(control) == "Shortcut for Go to file");
            chat.Text = quickOpen.Text;
            Dispatcher.UIThread.RunJobs();

            Button save = window.GetLogicalDescendants().OfType<Button>().Single(control =>
                AutomationProperties.GetName(control) == "Save validated keybindings");
            string text = string.Join('\n', window.GetLogicalDescendants().OfType<TextBlock>()
                .Select(block => block.Text));
            Assert.False(save.IsEnabled);
            Assert.Contains("conflicts", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Reset all keybindings to defaults");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Export keybindings as safe JSON");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Validate and import keybinding JSON");
            ComboBox inputMode = Assert.Single(window.GetLogicalDescendants().OfType<ComboBox>(),
                control => AutomationProperties.GetName(control) == "Editor keyboard input mode");
            Assert.Equal(EditorInputMode.Standard, inputMode.SelectedItem);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Agent_tool_settings_show_semantic_health_roles_and_authority()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore();
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.AgentTools);
            Dispatcher.UIThread.RunJobs();

            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("Roslyn semantic analysis", text, StringComparison.Ordinal);
            Assert.Contains("inspect_code_problems", text, StringComparison.Ordinal);
            Assert.Contains("Lead, Implementer, Reviewer", text, StringComparison.Ordinal);
            Assert.Contains("Authority: TrustedRead", text, StringComparison.Ordinal);
            Assert.Contains("External MCP sources", text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Privacy_settings_make_unlimited_default_and_cost_control_opt_ins_prominent()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore();
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.PrivacyAndLimits);
            Dispatcher.UIThread.RunJobs();

            ComboBox mode = Assert.Single(window.GetLogicalDescendants().OfType<ComboBox>(),
                item => AutomationProperties.GetName(item) == "Default remote spending mode");
            Assert.Equal(3, mode.ItemsSource!.Cast<object>().Count());
            Assert.Contains("Unlimited", mode.SelectedItem?.ToString(), StringComparison.Ordinal);
            Assert.Contains("Opt into a cap or local-only", string.Join('\n', window
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Mcp_settings_manage_stateless_connections_from_the_first_slice()
    {
        McpSettingsService mcp = new();
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            mcpSettingsService: mcp);
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.McpConnections);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Add connection"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Refresh active connections"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New MCP connection kind");
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New Harness control bearer token");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New Harness control client ID");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "New Harness control allowed tool IDs");
            ComboBox kind = Assert.Single(window.GetLogicalDescendants().OfType<ComboBox>(),
                control => AutomationProperties.GetName(control) == "New MCP connection kind");
            kind.SelectedItem = McpConnectionKind.HarnessControl;
            Dispatcher.UIThread.RunJobs();
            TextBox allowedTools = Assert.Single(
                window.GetLogicalDescendants().OfType<TextBox>(),
                control => AutomationProperties.GetName(control) ==
                    "New Harness control allowed tool IDs");
            Assert.Contains("harness_create_goal", allowedTools.Text,
                StringComparison.Ordinal);
            Assert.Contains("harness_decide_commit", allowedTools.Text,
                StringComparison.Ordinal);
            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("2026-07-28", text, StringComparison.Ordinal);
            Assert.Contains("1 eligible", text, StringComparison.Ordinal);
            Assert.Contains("fail closed", text, StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Inbound_mcp_settings_expose_accessible_names_for_policy_and_limit_fields()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore();
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.InboundMcp);
            Dispatcher.UIThread.RunJobs();

            string[] expectedNames =
            [
                "Allowed inbound MCP client IDs",
                "Allowed inbound MCP tool IDs",
                "Inbound MCP tool IDs requiring explicit approval",
                "Inbound MCP request timeout in seconds",
                "Inbound MCP result limit",
                "Inbound MCP audit retention",
            ];
            string[] actualNames = window.GetLogicalDescendants()
                .OfType<Control>()
                .Select(AutomationProperties.GetName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();

            Assert.All(expectedNames, name => Assert.Contains(name, actualNames));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Research_settings_expose_sources_offline_cache_dependency_and_explicit_export_controls()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            researchSettingsService: new ResearchSettingsService());
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.DocumentationAndDependencies);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Documentation index roots, one absolute path per line");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "MCP documentation tools, one connection/tool per line");
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Look up documentation"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Inspect dependency graph"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Preview package + SBOM diff"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Export current SBOM…"));
            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("exact local/package docs → local index → configured MCP → web", text,
                StringComparison.Ordinal);
            Assert.Contains("Cache: 3 entries", text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Visual_settings_expose_consent_privacy_limits_and_exact_frame_controls()
    {
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            visualCaptureService: new VisualCaptureService());
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.VisualVerification);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Enable visual verification capture");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Allow remote model access to visual captures");
            Assert.Contains(window.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetName(control) == "Capture one visual verification frame");
            string text = string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text));
            Assert.Contains("XDG portal v3 available", text, StringComparison.Ordinal);
            Assert.Contains("Off by default", text, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Plan_generation_shows_only_lead_compatible_models_and_prefers_configured_lead()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            GoalModelCandidate configuredLead = Candidate(
                "OpenRouter", "lead", ModelAccess.Remote, [AgentRole.Lead]);
            PlanGenerationDialog dialog = new(
                [
                    Candidate("Ollama", "plain", ModelAccess.Local, []),
                    Candidate("Ollama", "review", ModelAccess.Local, [AgentRole.Reviewer]),
                    configuredLead,
                ],
                configuredLead,
                "Disclosure");
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            AutoCompleteBox models = Assert.Single(
                dialog.GetLogicalDescendants().OfType<AutoCompleteBox>());
            Assert.True(models.IsVisible);
            Assert.True(models.Bounds.Height > 0);
            object model = Assert.Single(models.ItemsSource!.Cast<object>());
            Assert.Contains("OpenRouter/lead", models.SelectedItem?.ToString(),
                StringComparison.Ordinal);
            Assert.Contains("OpenRouter/lead", models.Text, StringComparison.Ordinal);
            Assert.Equal("Search provider or model", models.PlaceholderText);
            Assert.True(models.ItemFilter!("openrouter", model));
            Assert.False(models.ItemFilter!("ollama", model));
            Button showAll = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Show all models");
            showAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(models.IsDropDownOpen);
            Assert.Null(models.SelectedItem);
            Assert.Equal(string.Empty, models.Text);
            Assert.Empty(dialog.GetLogicalDescendants().OfType<TextBox>());
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void Role_catalog_keeps_compatible_models_from_every_provider_and_access_class()
    {
        GoalModelCandidate local = Candidate(
            "Ollama", "local-lead", ModelAccess.Local, [AgentRole.Lead]);
        GoalModelCandidate remote = Candidate(
            "OpenRouter", "remote-lead", ModelAccess.Remote, [AgentRole.Lead]);
        GoalModelCandidate incompatible = Candidate(
            "OpenRouter", "reviewer", ModelAccess.Remote, [AgentRole.Reviewer]);

        Assert.Equal(
            [local, remote],
            ModelSelectionCatalog.ForRole([local, remote, incompatible], AgentRole.Lead));
    }

    [Fact]
    public async Task Remote_model_remains_visible_but_cannot_be_authorized_for_local_only_goal()
    {
        GoalView localOnly = ApprovedGoalShell().Goals.SelectedGoal! with
        {
            RemoteBudget = null,
        };
        GoalModelCandidate remote = Candidate(
            "OpenRouter", "remote-lead", ModelAccess.Remote, [AgentRole.Lead]);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            RemoteModelAuthorizationDialog dialog = new(localOnly, remote, AgentRole.Lead);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            Button authorize = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Enable remote spend first"));
            Assert.False(authorize.IsEnabled);
            Assert.Contains("currently local-only", string.Join('\n', dialog
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unlimited_goal_can_authorize_a_remote_model_without_adding_a_cap()
    {
        GoalView unlimited = ApprovedGoalShell().Goals.SelectedGoal! with
        {
            RemoteBudget = RemoteSpendPreference.Default.ToGoalBudget(),
        };
        GoalModelCandidate remote = Candidate(
            "OpenRouter", "remote-lead", ModelAccess.Remote, [AgentRole.Lead]);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            RemoteModelAuthorizationDialog dialog = new(unlimited, remote, AgentRole.Lead);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            Button authorize = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Use remote model"));
            Assert.True(authorize.IsEnabled);
            Assert.Contains("Unlimited", string.Join('\n', dialog
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workflow_retry_allows_model_only_retry_without_token_ceiling()
    {
        GoalModelCandidate reviewer = Candidate(
            "local", "reviewer", ModelAccess.Local, [AgentRole.Reviewer]);
        GoalModelCandidate leadOnly = Candidate(
            "local", "lead", ModelAccess.Local, [AgentRole.Lead]);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkflowRetryDialog dialog = new(
                GoalWorkflowRetryRole.Reviewer,
                [leadOnly, reviewer],
                reviewer,
                "The prior call was not replayed.");
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            AutoCompleteBox models = Assert.Single(
                dialog.GetLogicalDescendants().OfType<AutoCompleteBox>());
            Assert.Single(models.ItemsSource!.Cast<object>());
            TextBox guidance = Assert.Single(
                dialog.GetLogicalDescendants().OfType<TextBox>(),
                field => AutomationProperties.GetName(field) == "Guidance for Reviewer retry");
            guidance.Text = string.Empty;
            Button retry = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Retry Reviewer"));
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(reviewer, dialog.Result?.Model);
            Assert.Null(dialog.Result?.Guidance);
        }, CancellationToken.None);
    }

    private static GoalModelCandidate Candidate(
        string provider,
        string model,
        ModelAccess access,
        IReadOnlyList<AgentRole> supportedRoles) => new(
        new(provider),
        new(model),
        access,
        [new("tools")],
        supportedRoles,
        null,
        null,
        null,
        null);

    [Fact]
    public void Closing_a_document_decision_dialog_defaults_to_cancel()
    {
        Assert.Equal(WorkbenchUnsavedDecision.Cancel, default(WorkbenchUnsavedDecision));
        Assert.Equal(WorkbenchConflictDecision.Cancel, default(WorkbenchConflictDecision));
    }

    [Fact]
    public async Task Markdown_content_renders_without_raw_provider_markup()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = MarkdownContentView.Create(
                "# Answer\n\nI am **Gemma 4** 😊</blockquote>\n\n```csharp\nvar answer = 4;\n```",
                _ => Brushes.Transparent);
            Window window = new() { Content = content };
            window.Show();

            string rendered = string.Join('\n', window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("Gemma 4", rendered, StringComparison.Ordinal);
            Assert.Contains("var answer = 4;", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("**", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("blockquote", rendered, StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Message_card_background_tracks_effective_theme_resources()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Application application = Assert.IsType<PresentationTestApplication>(Application.Current);
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.Panel)] =
                new SolidColorBrush(Colors.White);
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.AccentSoft)] =
                new SolidColorBrush(Colors.LightCyan);
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.Border)] =
                new SolidColorBrush(Colors.Gray);
            Border assistant = new();
            assistant.Classes.Add("message-card");
            Border user = new();
            user.Classes.Add("message-card");
            user.Classes.Add("user");
            Window window = new()
            {
                Content = new StackPanel { Children = { assistant, user } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(assistant.Background).Color);
            Assert.Equal(Colors.LightCyan, Assert.IsType<SolidColorBrush>(user.Background).Color);

            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.Panel)] =
                new SolidColorBrush(Color.Parse("#1B1B22"));
            application.Resources[HarnessThemeResources.Key(UiThemeColorToken.AccentSoft)] =
                new SolidColorBrush(Color.Parse("#173E43"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                Color.Parse("#1B1B22"),
                Assert.IsType<SolidColorBrush>(assistant.Background).Color);
            Assert.Equal(
                Color.Parse("#173E43"),
                Assert.IsType<SolidColorBrush>(user.Background).Color);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Code_editor_loads_with_required_style_and_real_text()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TextEditor editor = CodeEditorView.Create("diff --git a/App.cs b/App.cs");
            Window window = new() { Content = editor };
            window.Show();

            Assert.Equal("diff --git a/App.cs b/App.cs", editor.Text);
            Assert.True(editor.IsReadOnly);
            Assert.True(editor.ShowLineNumbers);
            Assert.True(editor.Options.HighlightCurrentLine);
            Assert.True(editor.Options.EnableRectangularSelection);
            Assert.True(editor.Options.AllowScrollBelowDocument);
            Assert.NotNull(editor.Template);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Semantic_editor_renderer_applies_exact_spans_occurrences_and_folding()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TextEditor editor = CodeEditorView.Create("class Sample\n{\n    int value;\n}\n",
                isReadOnly: false, path: "Sample.cs");
            Window window = new() { Content = editor };
            window.Show();
            using CodeSemanticRenderer renderer = new(editor);
            WorkbenchCodeLens? invoked = null;
            renderer.CodeLensInvoked += (_, args) => invoked = args.Lens;
            renderer.SetPresentation(new(
                new("session"), new("Sample.cs"), new(1), WorkbenchCodeResultState.Ready,
                [new(new(new(0, 0), new(0, 5)), WorkbenchCodeClassificationKind.Keyword),
                 new(new(new(0, 6), new(0, 12)), WorkbenchCodeClassificationKind.Type)],
                [new(new(new(0, 0), new(3, 1)), WorkbenchCodeFoldingKind.Type,
                    new("Sample …"), false)],
                [new(WorkbenchCodeSymbolKind.Class, new("Sample"),
                    new(new(0, 0), new(3, 1)), new(new(0, 6), new(0, 12)), 0)],
                [new(WorkbenchCodeSymbolKind.Class, new("Sample"),
                    new(new(0, 6), new(0, 12)))],
                [new(new(2, 13), WorkbenchCodeInlayHintKind.InferredType,
                    new(": int"), new("Inferred type: int"))],
                [new(new(0, 0), new(0, 6), WorkbenchCodeLensKind.References,
                    new("Find references"), false)],
                false, []));
            renderer.SetOccurrences(
                [new(new(new(2, 8), new(2, 13)), WorkbenchCodeOccurrenceKind.Definition)]);

            Assert.Equal(2, renderer.ClassificationCount);
            Assert.Equal(1, renderer.FoldingCount);
            Assert.Equal(1, renderer.OccurrenceCount);
            Assert.Equal(1, renderer.InlayHintCount);
            Assert.Equal(1, renderer.CodeLensCount);
            VisualLineElementGenerator generator = editor.TextArea.TextView.ElementGenerators[^1];
            Assert.Equal(0, generator.GetFirstInterestedOffset(0));
            InlineObjectElement inline = Assert.IsType<InlineObjectElement>(
                generator.ConstructElement(0));
            Button lens = Assert.Single(
                Assert.IsType<StackPanel>(inline.Element).Children.OfType<Button>());
            lens.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(WorkbenchCodeLensKind.References, invoked?.Kind);
            Assert.Equal(new WorkbenchCodePosition(0, 6), invoked?.Target);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_exposes_presented_code_lenses_as_accessible_actions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDocumentView view = new(
                new("workspace"),
                null,
                null,
                new("src/Program.cs"),
                new("internal static class Program { public static void Main() { } }"),
                null,
                new(62),
                IsTruncated: false,
                WorkbenchDocumentAccess.Editable,
                "Editing the active trusted workspace.",
                ErrorCode: null,
                Error: null);
            using SourceEditorSurface surface = SourceEditorSurface.Create(
                view,
                KeybindingSettingsSnapshot.Default);
            WorkbenchCodeLens expected = new(
                new(0, 0),
                new(0, 47),
                WorkbenchCodeLensKind.Run,
                new("Run project"),
                true);
            WorkbenchCodeLens? invoked = null;
            surface.CodeLensInvoked += (_, args) => invoked = args.Lens;
            Window window = new() { Width = 1280, Height = 800, Content = surface.Control };
            window.Show();

            surface.UpdateDocumentPresentation(new(
                new("session"),
                new("src/Program.cs"),
                new(1),
                WorkbenchCodeResultState.Ready,
                [],
                [],
                [],
                [],
                [],
                [expected],
                false,
                []));

            Button menu = Assert.Single(surface.Control.GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Show CodeLens actions");
            Assert.True(menu.IsEnabled);
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Border flyoutContent = Assert.IsType<Border>(
                Assert.IsType<Flyout>(menu.Flyout).Content);
            Button action = Assert.Single(
                Assert.IsType<StackPanel>(flyoutContent.Child).Children.OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Run project at line 1");
            Assert.Equal("Run project · L1", action.Content);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(expected, invoked);

            surface.UpdateDocumentPresentation(new(
                new("session"),
                new("src/Program.cs"),
                new(1),
                WorkbenchCodeResultState.Ready,
                [], [], [], [], [], [], false, []));
            Assert.False(menu.IsEnabled);
            Assert.DoesNotContain(
                Assert.IsType<StackPanel>(flyoutContent.Child).Children.OfType<Button>(),
                button => AutomationProperties.GetName(button)?.StartsWith(
                    "Run project at line", StringComparison.Ordinal) is true);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reactivating_a_source_document_recovers_an_initial_presentation_without_actions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            bool ready = false;
            CodeIntelligenceService codeIntelligence = new()
            {
                Presentation = request => ready
                    ? new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        [], [], [], [], [],
                        [new(new(0, 0), new(0, 6), WorkbenchCodeLensKind.References,
                            new("Find references"), false)],
                        false,
                        [])
                    : new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        [], [], [], [], [], [], false, []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();

            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            IDockable source = workbench.Documents.ActiveDockable!;
            workbench.OpenFileAsync("src/App.csproj").AsTask().GetAwaiter().GetResult();
            ready = true;
            workbench.ReactivateDocumentForTest(source);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                if (source.Context is Control current &&
                    current.GetVisualDescendants().OfType<Button>().Any(button =>
                        AutomationProperties.GetName(button) == "Show CodeLens actions" &&
                        button.IsEnabled))
                {
                    break;
                }
                Thread.Sleep(10);
            }

            Control content = Assert.IsAssignableFrom<Control>(source.Context);
            Assert.Contains(content.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Show CodeLens actions" &&
                button.IsEnabled);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Comment", UiThemeColorToken.CodeComment)]
    [InlineData("StringInterpolation", UiThemeColorToken.CodeString)]
    [InlineData("Digits", UiThemeColorToken.CodeNumber)]
    [InlineData("MethodCall", UiThemeColorToken.CodeMethod)]
    [InlineData("Preprocessor", UiThemeColorToken.CodePreprocessor)]
    [InlineData("Punctuation", UiThemeColorToken.CodePunctuation)]
    [InlineData("ValueTypeKeywords", UiThemeColorToken.CodeType)]
    [InlineData("Visibility", UiThemeColorToken.CodeKeyword)]
    public void Csharp_highlighting_uses_distinct_semantic_theme_colors(
        string highlightingCategory,
        UiThemeColorToken expected)
    {
        Assert.Equal(expected, CodeEditorView.ThemeTokenFor(highlightingCategory));
    }

    [Fact]
    public async Task Empty_workbench_offers_a_direct_workspace_folder_action()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            bool requested = false;
            bool browseImmediately = false;
            WorkbenchDockHost workbench = CreateWorkbench(
                AvaloniaShellState.Initial with { IsLoading = false },
                new(),
                manageWorkspace: browse =>
                {
                    requested = true;
                    browseImmediately = browse;
                    return Task.CompletedTask;
                });
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.Update(AvaloniaShellState.Initial with { IsLoading = false });

            Button action = workbench.OverviewAction;
            Assert.Equal("Open workspace", action.Content);
            Assert.Contains("primary", action.Classes);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(requested);
            Assert.True(browseImmediately);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Docked_workbench_opens_real_workspace_file_as_center_document()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkspaceView workspace = new(
                "workspace-1",
                "/work/repository",
                "repository",
                "/work/repository/Harness.slnx",
                IsTrusted: true,
                IsActive: true,
                "main",
                IsDirty: true);
            AvaloniaShellState shell = AvaloniaShellState.Initial with
            {
                Workspaces = WorkspaceManagementState.Initial with { Registered = [workspace] },
                IsLoading = false,
            };
            DocumentService documents = new();
            WorkbenchDockHost workbench = new(
                new RunOutputService(),
                new InspectionService(),
                documents,
                new CodeIntelligenceService(),
                new LayoutService(),
                new DocumentPrompt(),
                () => shell,
                new TextBlock { Text = "Workspace" },
                new TextBlock { Text = "Conversation" },
                new TextBlock { Text = "Goal context" },
                CancellationToken.None);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.OpenFileAsync("src/Feature.cs").AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            using Bitmap rendered = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());

            Assert.IsType<DockControl>(workbench.Control);
            Assert.Equal(
                ["document.workspace.overview", "document.file.workspace-1.original.src/App.cs",
                    "document.file.workspace-1.original.src/Feature.cs"],
                workbench.Documents.VisibleDockables?.Select(item => item.Id).ToArray() ?? []);
            DocumentTabStripItem[] documentTabs = window.GetVisualDescendants()
                .OfType<DocumentTabStripItem>()
                .ToArray();
            Assert.Equal(3, documentTabs.Length);
            Assert.Equal(
                ["Workspace overview", "App.cs", "Feature.cs"],
                documentTabs.Select(tab => AutomationProperties.GetName(tab) ?? string.Empty).ToArray());
            Assert.All(documentTabs, tab => Assert.Equal(
                AccessibilityView.Content,
                AutomationProperties.GetAccessibilityView(tab)));
            ComboBox documentSwitcher = workbench.DocumentSwitcher;
            Assert.Equal("Open editor documents", AutomationProperties.GetName(documentSwitcher));
            Assert.Equal(
                ["Workspace overview", "App.cs", "Feature.cs"],
                Assert.IsAssignableFrom<IEnumerable<object>>(documentSwitcher.ItemsSource)
                    .Select(item => item.ToString() ?? string.Empty)
                    .ToArray());
            documentSwitcher.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("App.cs", workbench.Documents.ActiveDockable?.Title);
            Button focusEditor = Assert.Single(
                Assert.IsType<StackPanel>(workbench.DocumentActions).Children.OfType<Button>());
            Assert.Equal(
                "Focus the active editor document",
                AutomationProperties.GetName(focusEditor));
            focusEditor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(workbench.ActiveSourceEditor, workbench.LastRequestedFocusTarget);
            Assert.Equal(7, DurableTools(workbench.Root).Count);
            Control documentContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            TextEditor editor = Assert.Single(
                documentContent.GetVisualDescendants().OfType<TextEditor>());
            Assert.Contains(editor, window.GetVisualDescendants().OfType<TextEditor>());
            Assert.Equal("namespace Example;", editor.Text);
            Assert.False(editor.IsReadOnly);
            string sourceChrome = string.Join('\n', documentContent.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("src › App.cs", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("Original workspace", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("EDITABLE", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("Ln 1, Col 1 · UTF-8 · No line break", sourceChrome, StringComparison.Ordinal);
            editor.Text = "namespace UserEdited;";
            Assert.True(workbench.SaveActiveSourceDocumentAsync().AsTask().GetAwaiter().GetResult());
            WorkbenchDocumentSaveRequest save = Assert.Single(documents.SaveRequests);
            Assert.Equal("workspace-1", save.WorkspaceId.Value);
            Assert.Null(save.GoalId);
            Assert.Equal("src/App.cs", save.Path.Value);
            Assert.Equal("namespace UserEdited;", save.Content.Value);
            Assert.NotNull(workbench.Control.Template);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Approved_goal_document_tracks_real_dirty_state_and_saves_with_its_baseline()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            TextEditor editor = Assert.IsType<TextEditor>(workbench.ActiveSourceEditor);
            Assert.False(editor.IsReadOnly);
            Assert.Contains("Editable source editor", AutomationProperties.GetName(editor), StringComparison.Ordinal);
            editor.Text = "namespace Changed;";

            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            Assert.True(workbench.Documents.ActiveDockable?.IsModified);
            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button[] documentActions = sourceContent.GetVisualDescendants()
                .OfType<Button>()
                .ToArray();
            Assert.Equal(
                ["Save", "Reload", "Close", "CodeLens", "Outline", "Symbols", "IntelliSense", "Symbol info", "Definition",
                    "Usages", "Implementations", "Inspect", "Quick fix…", "Transform"],
                documentActions.Select(item => item.Content?.ToString() ?? string.Empty).ToArray());
            Assert.All(documentActions, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item))));
            Assert.True(documentActions[0].IsEnabled);
            string sourceChrome = string.Join('\n', sourceContent.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("src › App.cs", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("harness/goal-1", sourceChrome, StringComparison.Ordinal);
            Assert.Contains("EDITABLE", sourceChrome, StringComparison.Ordinal);
            editor.Text = "one\ntwo";
            editor.CaretOffset = editor.Text.Length;
            Dispatcher.UIThread.RunJobs();
            sourceChrome = string.Join('\n', sourceContent.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text));
            Assert.Contains("Ln 2, Col 4 · UTF-8 · LF", sourceChrome, StringComparison.Ordinal);
            editor.Text = "namespace Changed;";
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.S,
                KeyModifiers = KeyModifiers.Control,
            });

            WorkbenchDocumentSaveRequest request = Assert.Single(documents.SaveRequests);
            Assert.Equal("goal-1", request.GoalId!.Value);
            Assert.Equal("src/App.cs", request.Path.Value);
            Assert.Equal("namespace Changed;", request.Content.Value);
            Assert.Equal(
                "7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4",
                request.ExpectedSha256?.Value);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            Assert.False(workbench.Documents.ActiveDockable?.IsModified);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dirty_document_switch_requires_cancel_or_discard_before_activation()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.Contains("src/App.cs", workbench.Documents.ActiveDockable?.Id, StringComparison.Ordinal);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            Assert.Equal(2, workbench.SourceDocumentCount);
            Assert.Contains("src/Other.cs", workbench.Documents.ActiveDockable?.Id, StringComparison.Ordinal);
            Assert.All(
                prompt.UnsavedPrompts,
                item => Assert.Equal(WorkbenchDocumentTransition.Switch, item.Transition));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dock_tab_activation_cannot_bypass_dirty_document_decisions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            IDockable app = Assert.Single(
                workbench.Documents.VisibleDockables!,
                item => item.Id?.Contains("src/App.cs", StringComparison.Ordinal) is true);
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved other";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.Factory.SetActiveDockable(app);
            Assert.Contains("src/Other.cs", workbench.Documents.ActiveDockable?.Id, StringComparison.Ordinal);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Save);
            workbench.Factory.SetActiveDockable(app);
            Assert.Same(app, workbench.Documents.ActiveDockable);
            Assert.Single(documents.SaveRequests);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Save_conflict_requires_explicit_overwrite_and_retries_against_the_observed_version()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            string current = new('c', 64);
            documents.SaveResults.Enqueue(new(
                new("workspace-1"),
                new("goal-1"),
                new("ignored-1"),
                new("src/App.cs"),
                new(new string('7', 64)),
                new(current),
                null,
                new(0),
                WorkbenchDocumentSaveOutcome.Conflict,
                "content_changed",
                "The file changed."));
            documents.SaveResults.Enqueue(new(
                new("workspace-1"),
                new("goal-1"),
                new("ignored-2"),
                new("src/App.cs"),
                new(current),
                new(current),
                new(new string('d', 64)),
                new(18),
                WorkbenchDocumentSaveOutcome.Saved,
                null,
                null));
            DocumentPrompt prompt = new();
            prompt.ConflictDecisions.Enqueue(WorkbenchConflictDecision.Overwrite);
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "namespace Changed;";

            Assert.True(workbench.SaveActiveSourceDocumentAsync().AsTask().GetAwaiter().GetResult());

            Assert.Equal(2, documents.SaveRequests.Count);
            Assert.Equal(current, documents.SaveRequests[1].ExpectedSha256?.Value);
            Assert.Single(prompt.ConflictPrompts);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dirty_close_and_application_exit_honor_save_discard_cancel_decisions()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentService documents = new() { Editable = true };
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents, prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.ActiveSourceEditor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.W,
                KeyModifiers = KeyModifiers.Control,
            });
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            Assert.False(workbench.PrepareForShutdownAsync().AsTask().GetAwaiter().GetResult());
            Assert.Equal(1, workbench.SourceDocumentCount);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            workbench.CloseActiveSourceDocumentAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(0, workbench.SourceDocumentCount);
            Assert.Equal(
                [WorkbenchDocumentTransition.Close, WorkbenchDocumentTransition.Exit,
                    WorkbenchDocumentTransition.Close],
                prompt.UnsavedPrompts.Select(item => item.Transition).ToArray());
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dock_close_chrome_cannot_remove_a_dirty_document_without_a_decision()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            DocumentPrompt prompt = new();
            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            WorkbenchDockHost workbench = CreateWorkbench(
                shell,
                new(),
                new() { Editable = true },
                prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            workbench.Factory.CloseDockable(workbench.Documents.ActiveDockable!);
            Assert.Equal(1, workbench.SourceDocumentCount);
            Dispatcher.UIThread.RunJobs();
            WorkbenchUnsavedPrompt close = Assert.Single(prompt.UnsavedPrompts);
            Assert.Equal(WorkbenchDocumentTransition.Close, close.Transition);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            Assert.Equal(1, workbench.SourceDocumentCount);
            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// The diff document renders decorated rows rather than one raw editor, so its content is
    /// read back from the rendered text rows.
    /// </summary>
    private static string RenderedDiffText(Window window, IDockable diff)
    {
        window.UpdateLayout();
        Control content = Assert.IsAssignableFrom<Control>(diff.Context);
        return string.Join(
            '\n',
            content.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text));
    }

    [Fact]
    public async Task Approved_goal_source_and_diff_share_context_and_keep_document_identity()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            InspectionService inspection = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                shell,
                new(),
                new() { Editable = true },
                inspection: inspection);
            Window window = new() { Content = workbench.Control };
            window.Show();

            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            IDockable source = workbench.Documents.ActiveDockable!;
            TextEditor sourceEditor = workbench.ActiveSourceEditor!;
            workbench.OpenDiffAsync().AsTask().GetAwaiter().GetResult();

            IDockable diff = workbench.Documents.ActiveDockable!;
            Assert.Equal("document.git.diff.workspace-1.goal-1", diff.Id);
            Assert.Equal("harness/goal-1 working diff", diff.Title);
            Assert.Contains("first diff", RenderedDiffText(window, diff), StringComparison.Ordinal);
            Assert.All(inspection.Requests, request => Assert.Equal("goal-1", request.GoalId?.Value));

            inspection.Diff = "refreshed diff";
            workbench.OpenDiffAsync().AsTask().GetAwaiter().GetResult();
            Assert.Same(diff, workbench.Documents.ActiveDockable);
            Assert.Contains("refreshed diff", RenderedDiffText(window, diff), StringComparison.Ordinal);

            workbench.Factory.SetActiveDockable(source);
            Assert.Same(sourceEditor, workbench.ActiveSourceEditor);
            workbench.Factory.SetActiveDockable(diff);
            workbench.Factory.CloseDockable(diff);
            Assert.DoesNotContain(workbench.Documents.VisibleDockables!, item => item.Id == diff.Id);
            workbench.Factory.SetActiveDockable(source);
            Assert.Same(sourceEditor, workbench.ActiveSourceEditor);
            Assert.Equal("namespace Example;", sourceEditor.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Representative_multi_project_tabs_retain_cached_editors_during_switching()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true });
            Window window = new() { Content = workbench.Control };
            window.Show();
            string[] paths = Enumerable.Range(1, 6)
                .SelectMany(project => new[]
                {
                    $"src/Project{project}/Program.cs",
                    $"src/Project{project}/Services/Worker.cs",
                    $"tests/Project{project}.Tests/WorkerTests.cs",
                })
                .ToArray();
            Dictionary<string, TextEditor> editors = new(StringComparer.Ordinal);
            Stopwatch opening = Stopwatch.StartNew();
            foreach (string item in paths)
            {
                workbench.OpenFileAsync(item).AsTask().GetAwaiter().GetResult();
                editors.Add(workbench.Documents.ActiveDockable!.Id!, workbench.ActiveSourceEditor!);
            }

            opening.Stop();
            IDockable[] documents = workbench.Documents.VisibleDockables!
                .Where(item => item.Id?.StartsWith("document.file.", StringComparison.Ordinal) is true)
                .ToArray();
            Stopwatch switching = Stopwatch.StartNew();
            for (int pass = 0; pass < 100; pass++)
            {
                foreach (IDockable document in documents)
                {
                    workbench.Factory.SetActiveDockable(document);
                    Assert.Same(editors[document.Id!], workbench.ActiveSourceEditor);
                }
            }

            switching.Stop();
            Assert.Equal(18, documents.Length);
            Assert.True(opening.Elapsed < TimeSpan.FromSeconds(10),
                $"Opening 18 representative documents took {opening.Elapsed}.");
            Assert.True(switching.Elapsed < TimeSpan.FromSeconds(5),
                $"Switching 1,800 cached tabs took {switching.Elapsed}.");
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Compact_viewport_collapses_tools_and_keyboard_commands_restore_access()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Width = 800, Height = 600, Content = workbench.Control };
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            workbench.ApplyViewport(800, 520);

            IToolDock left = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Right);
            IToolDock bottom = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Bottom);
            Assert.True(workbench.IsCompactViewport);
            Assert.False(left.IsExpanded);
            Assert.False(right.IsExpanded);
            Assert.False(bottom.IsExpanded);
            Assert.True(left.MaxWidth <= 76);
            Assert.True(right.MaxWidth <= 76);
            Assert.True(bottom.MaxHeight <= 84);
            Assert.All(left.VisibleDockables!, item =>
                Assert.False(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));
            Assert.All(right.VisibleDockables!, item =>
                Assert.False(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));
            Assert.All(bottom.VisibleDockables!, item =>
                Assert.False(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));

            Assert.True(workbench.Control.Focus());
            window.KeyPressQwerty(
                PhysicalKey.G,
                RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(right.IsExpanded);
            Assert.Equal(WorkbenchDockIds.GitTool, right.ActiveDockable?.Id);
            Assert.All(right.VisibleDockables!, item =>
                Assert.True(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));

            window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            Assert.True(bottom.IsExpanded);
            Assert.Equal(WorkbenchDockIds.RunOutputTool, bottom.ActiveDockable?.Id);
            Assert.All(bottom.VisibleDockables!, item =>
                Assert.True(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));

            window.KeyPressQwerty(
                PhysicalKey.M,
                RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(WorkbenchDockIds.ProblemsTool, bottom.ActiveDockable?.Id);

            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(left.IsExpanded);
            Assert.Equal(WorkbenchDockIds.FilesTool, left.ActiveDockable?.Id);
            Assert.All(left.VisibleDockables!, item =>
                Assert.True(Assert.IsAssignableFrom<Control>(item.Context).IsVisible));
            Assert.True(workbench.LastRequestedFocusTarget?.Focusable);

            workbench.ApplyViewport(1280, 800);
            Assert.False(workbench.IsCompactViewport);
            Assert.True(left.IsExpanded);
            Assert.True(right.IsExpanded);
            Assert.True(bottom.IsExpanded);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_exposes_semantic_outline_and_clickable_breadcrumbs()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Presentation = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(new(new(0, 0), new(0, 9)),
                        WorkbenchCodeClassificationKind.Keyword)],
                    [],
                    [new(WorkbenchCodeSymbolKind.Namespace, new("Example"),
                        new(new(0, 0), new(0, 18)),
                        new(new(0, 10), new(0, 17)), 0)],
                    [new(WorkbenchCodeSymbolKind.Namespace, new("Example"),
                        new(new(0, 10), new(0, 17)))],
                    [],
                    [],
                    false,
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                if (workbench.Documents.ActiveDockable?.Context is Control current &&
                    current.GetVisualDescendants().OfType<Button>().Any(button =>
                        Equals(button.Content, "Example")))
                {
                    break;
                }
                Thread.Sleep(10);
            }

            Control source = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button outline = Assert.Single(source.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Outline"));
            Button breadcrumb = Assert.Single(source.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Example"));
            Assert.True(outline.IsEnabled);
            Assert.Equal("Go to Example", AutomationProperties.GetName(breadcrumb));

            breadcrumb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(11, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_opens_accessible_roslyn_completion_and_quick_info_from_keyboard()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Completions = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    new("list-1"),
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [new(
                        new("item-1"),
                        new("Example"),
                        new("Example"),
                        new("Example"),
                        new("namespace"),
                        WorkbenchCodeSymbolKind.Namespace,
                        ['\t', '\n', '('],
                        IsRecommended: false)],
                    []),
                CompletionCommit = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        new(request.Snapshot.Position, request.Snapshot.Position),
                        new("Example"))],
                    new(
                        request.Snapshot.Position.Line,
                        request.Snapshot.Position.Character + "Example".Length),
                    []),
                QuickInfo = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    new(snapshot.Position, snapshot.Position),
                    [new("namespace Example")],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            editor.CaretOffset = editor.Text.Length;

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, workbench.ActiveCompletionItemCount);
            CompletionWindow completionWindow = workbench.ActiveCompletionWindow!;
            completionWindow.CompletionList.CompletionData[0].Complete(
                editor.TextArea,
                new SimpleSegment(editor.CaretOffset, 0),
                EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.EndsWith("Example", editor.Text, StringComparison.Ordinal);
            RoslynCompletionData completion = new(
                codeIntelligence.Completions(new(
                    new(
                        new("session-1"),
                        new("src/App.cs"),
                        new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                        new(1),
                        new(editor.Text),
                        new(0, 0)),
                    WorkbenchCodeCompletionTriggerKind.Invoke,
                    null)).Items[0],
                (_, _) => { });
            Assert.Contains(
                "Namespace Example namespace",
                AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(completion.Content)),
                StringComparison.Ordinal);
            char? committedWith = null;
            RoslynCompletionData commitData = new(
                codeIntelligence.Completions(new(
                    new(
                        new("session-1"),
                        new("src/App.cs"),
                        new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                        new(1),
                        new(editor.Text),
                        new(0, 0)),
                    WorkbenchCodeCompletionTriggerKind.Invoke,
                    null)).Items[0],
                (_, character) => committedWith = character);
            commitData.CompleteWithCharacter('(');
            Assert.Equal('(', committedWith);
            RoslynOverloadProvider overloads = new(new(
                new("session-1"),
                new("src/App.cs"),
                new(1),
                WorkbenchCodeResultState.Ready,
                [new(
                    new("void Run(string text, int count)"),
                    new("Runs the operation."),
                    [new(new("text"), new("string text"), new(string.Empty)),
                     new(new("count"), new("int count"), new(string.Empty))])],
                0,
                1,
                []));
            Assert.Contains(
                "parameter 2",
                AutomationProperties.GetName(
                    Assert.IsAssignableFrom<Control>(overloads.CurrentHeader)),
                StringComparison.OrdinalIgnoreCase);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.K,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.True(workbench.ActiveQuickInfoIsOpen);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_dispatches_the_saved_completion_keybinding_only()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Completions = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    new("custom-keys"),
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [new(new("item"), new("Example"), new("Example"), new("Example"),
                        new("namespace"), WorkbenchCodeSymbolKind.Namespace, ['\t'], false)],
                    []),
            };
            KeybindingSettingsSnapshot defaults = KeybindingSettingsSnapshot.Default;
            KeybindingSettingsSnapshot custom = defaults with
            {
                Bindings = defaults.Bindings.Select(binding =>
                    binding.Definition.Command is KeybindingCommand.ShowCompletion
                        ? binding with
                        {
                            Gestures = [new(
                                KeybindingModifiers.Control | KeybindingModifiers.Shift,
                                KeybindingKey.Q)],
                        }
                        : binding).ToArray(),
                UsesDefaults = false,
            };
            AvaloniaShellState shell = TrustedShell() with
            {
                Settings = TrustedShell().Settings with { KeybindingSettings = custom },
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                shell, new(), codeIntelligence: codeIntelligence);
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            Control source = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button completion = Assert.Single(source.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "IntelliSense"));
            Assert.Contains("Ctrl+Shift+Q", Assert.IsType<string>(ToolTip.GetTip(completion)),
                StringComparison.Ordinal);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Space,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, workbench.ActiveCompletionItemCount);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Q,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, workbench.ActiveCompletionItemCount);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_editor_applies_vim_mode_to_the_live_editable_buffer_and_reports_mode()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            KeybindingSettingsSnapshot vim = KeybindingSettingsSnapshot.Default with
            {
                InputMode = EditorInputMode.Vim,
            };
            AvaloniaShellState shell = TrustedShell() with
            {
                Settings = TrustedShell().Settings with { KeybindingSettings = vim },
            };
            DocumentService documents = new() { Content = "one two\nthree\n" };
            WorkbenchDockHost workbench = CreateWorkbench(shell, new(), documents);
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            Control source = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);

            Assert.Contains("VIM NORMAL", string.Join('\n', source
                .GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.W,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, editor.CaretOffset);

            KeyEventArgs deleteKey = new()
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.X,
            };
            editor.RaiseEvent(deleteKey);
            Dispatcher.UIThread.RunJobs();
            Assert.True(deleteKey.Handled);
            Assert.Equal("one wo\nthree\n", editor.Text);

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.I,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("VIM INSERT", string.Join('\n', source
                .GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("VIM NORMAL", string.Join('\n', source
                .GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F12_definition_navigation_moves_to_the_exact_source_range()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Definition = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        WorkbenchCodeDestinationKind.Source,
                        new("Example"),
                        snapshot.Path,
                        new(new(0, 2), new(0, 9)))],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;

            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F12,
                KeyModifiers = KeyModifiers.None,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, workbench.ActiveSourceEditor?.TextArea.Caret.Line);
            Assert.Equal(3, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F12_metadata_definition_opens_labeled_read_only_decompiled_source()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            WorkbenchCodeVirtualDocumentId id = new(new string('a', 64));
            CodeIntelligenceService codeIntelligence = new()
            {
                Definition = snapshot => new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(WorkbenchCodeDestinationKind.Metadata, new("System.String.Empty"),
                        null, null, id)], []),
                VirtualDocument = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    request.Id,
                    WorkbenchCodeVirtualDocumentKind.DecompiledSource,
                    new("String · decompiled"),
                    new("public sealed class String { public int Length => 42; }"),
                    new(new(0, 20), new(0, 26)),
                    new(new("Sample"), new("version"), new("net10.0"), new("Debug"),
                        new("System.Runtime, Version=10.0.0.0"), new(new string('b', 64))),
                    IsReadOnly: true,
                    []),
            };
            LayoutService layouts = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), layouts, codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.ActiveSourceEditor!.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F12,
                KeyModifiers = KeyModifiers.None,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, workbench.VirtualDocumentCount);
            Assert.True(workbench.ActiveVirtualEditor!.IsReadOnly);
            Assert.Contains("public sealed class String", workbench.ActiveVirtualEditor.Text,
                StringComparison.Ordinal);
            Assert.Contains("Decompiled source", AutomationProperties.GetName(
                workbench.ActiveVirtualEditor), StringComparison.Ordinal);
            Assert.Contains("read-only", workbench.Documents.ActiveDockable!.Title,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Assert.IsAssignableFrom<Control>(
                        workbench.Documents.ActiveDockable.Context)
                    .GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("Compilation " + new string('b', 64),
                    StringComparison.Ordinal) == true);
            workbench.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.NotNull(layouts.Stored);
            Assert.DoesNotContain("virtual:", layouts.Stored, StringComparison.Ordinal);
            Assert.DoesNotContain("public sealed class String", layouts.Stored,
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Exact_context_inspection_opens_a_transient_read_only_document()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Inspection = request => new(
                    request.Snapshot.SessionId, request.Snapshot.Path,
                    request.Snapshot.BufferVersion, WorkbenchCodeResultState.Ready,
                    request.Kind, new("Symbol · Run"), new("Kind: Method\nDisplay: void C.Run()"),
                    new(new("Sample"), new("project-version"), new("net10.0"), new("Debug"),
                        new("Sample, Version=1.0.0.0"), new(new string('c', 64))),
                    IsReadOnly: true, IsTruncated: false, []),
            };
            LayoutService layouts = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), layouts, codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.InspectActiveDocumentAsync(WorkbenchCodeInspectionKind.Symbol)
                .AsTask().GetAwaiter().GetResult();

            TextEditor editor = Assert.IsType<TextEditor>(workbench.Documents.ActiveDockable!.Context);
            Assert.True(editor.IsReadOnly);
            Assert.Contains("Kind: Method", editor.Text, StringComparison.Ordinal);
            Assert.Contains("read-only", workbench.Documents.ActiveDockable.Title,
                StringComparison.OrdinalIgnoreCase);
            workbench.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.NotNull(layouts.Stored);
            Assert.DoesNotContain("inspection:", layouts.Stored, StringComparison.Ordinal);
            Assert.DoesNotContain("Kind: Method", layouts.Stored, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_toolbar_exposes_intellisense_navigation_usages_and_implementations()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                Implementations = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(
                        WorkbenchCodeDestinationKind.Source,
                        new("Example implementation"),
                        snapshot.Path,
                        new(new(0, 5), new(0, 12)))],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            string[] actionLabels =
            [
                "IntelliSense",
                "Symbol info",
                "Definition",
                "Usages",
                "Implementations",
            ];
            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Button[] actions = sourceContent.GetVisualDescendants().OfType<Button>()
                .Where(button => actionLabels.Contains(button.Content?.ToString()))
                .ToArray();
            Assert.Equal(actionLabels.Length, actions.Length);
            Assert.All(actions, action => Assert.True(action.IsEnabled));
            Assert.Contains(actions, action =>
                AutomationProperties.GetName(action) == "Show IntelliSense for src/App.cs");

            Button implementations = Assert.Single(actions, action =>
                Equals(action.Content, "Implementations"));
            implementations.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, codeIntelligence.ImplementationCallCount);
            Assert.Equal(1, workbench.ActiveSourceEditor?.TextArea.Caret.Line);
            Assert.Equal(6, workbench.ActiveSourceEditor?.TextArea.Caret.Column);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_rename_uses_the_shared_fingerprinted_atomic_operation()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            MutationService mutations = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                mutationService: mutations);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            editor.CaretOffset = editor.Text.IndexOf("Example", StringComparison.Ordinal) + 2;

            PendingWorkbenchRename pending = Assert.IsType<PendingWorkbenchRename>(
                workbench.PreviewActiveRenameAsync("Renamed").AsTask().GetAwaiter().GetResult());
            RenameSymbolApplyView applied = Assert.IsType<RenameSymbolApplyView>(
                workbench.ApplyActiveRenameAsync(pending).AsTask().GetAwaiter().GetResult());

            Assert.Equal("Renamed", pending.Preview.NewName.Value);
            Assert.Equal("goal-1", mutations.PreviewRequest?.GoalId);
            Assert.Equal("src/App.cs", mutations.PreviewRequest?.Path.Value);
            Assert.Equal(1, mutations.ApplyCallCount);
            Assert.Null(applied.ErrorCode);
            Assert.Equal("namespace Renamed;", editor.Text);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_format_document_applies_one_undoable_live_buffer_change()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [new(
                        request.Snapshot.Path,
                        request.Snapshot.BaselineHash,
                        request.Snapshot.Text,
                        new("namespace Example;\n"),
                        1)],
                    [],
                    [],
                    new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            string original = editor.Text;

            workbench.TransformActiveDocumentAsync(
                WorkbenchCodeDocumentTransformationKind.FormatDocument)
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal("namespace Example;\n", editor.Text);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            editor.Document.UndoStack.Undo();
            Assert.Equal(original, editor.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_formats_pasted_text_through_the_guarded_Roslyn_preview()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            WorkbenchCodeDocumentTransformationPreviewRequest? observed = null;
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request =>
                {
                    observed = request;
                    return new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        WorkbenchCodeTransformationDisposition.Ready,
                        request.Kind,
                        request.Range,
                        [new(
                            request.Snapshot.Path,
                            request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new("namespace Example;\n"),
                            1)],
                        [],
                        [],
                        new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                        [],
                        ImportNamespace: null,
                        FormattingTrigger: request.FormattingTrigger);
                },
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            editor.Text += "abc";
            editor.CaretOffset = editor.Text.Length;

            workbench.HandleActivePasteAsync(new(new(0, 18), new(0, 21)))
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal(WorkbenchCodeDocumentTransformationKind.FormatPaste, observed?.Kind);
            Assert.Equal(WorkbenchCodeFormattingTrigger.Paste, observed?.FormattingTrigger);
            Assert.NotNull(observed?.Range);
            Assert.Equal("namespace Example;\n", editor.Text);
            Assert.True(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_quick_fix_discovers_typed_missing_import_choices_at_the_caret()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            CodeIntelligenceService codeIntelligence = new()
            {
                MissingImports = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(new("System.Text"), new("System.Text.StringBuilder"),
                        new(new(0, 0), new(0, 7)))],
                    []),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.ShowActiveQuickFixesAsync().AsTask().GetAwaiter().GetResult();

            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Assert.Contains(sourceContent.GetVisualDescendants().OfType<Button>(), button =>
                button.Content?.ToString() == "Quick fix…" &&
                AutomationProperties.GetName(button)?.StartsWith(
                    "Show quick fixes", StringComparison.Ordinal) is true);
            Assert.Contains(sourceContent.GetLogicalDescendants().OfType<TextBlock>(), block =>
                block.Text?.Contains("1 Roslyn quick fix", StringComparison.Ordinal) is true);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_applies_a_closed_code_action_as_one_undoable_buffer_change()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            WorkbenchCodeDocumentTransformationPreviewRequest? observed = null;
            const string actionId =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            CodeIntelligenceService codeIntelligence = new()
            {
                CodeActions = snapshot => new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [new(new(actionId), WorkbenchClosedCodeActionKind.ImplementInterface,
                        WorkbenchCodeActionScope.Occurrence, new("Implement interface"),
                        new("CS0535"), new(new(0, 0), new(0, 7)))],
                    []),
                DocumentTransformations = request =>
                {
                    observed = request;
                    return new(
                        request.Snapshot.SessionId,
                        request.Snapshot.Path,
                        request.Snapshot.BufferVersion,
                        WorkbenchCodeResultState.Ready,
                        WorkbenchCodeTransformationDisposition.Ready,
                        request.Kind,
                        request.Range,
                        [new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new(request.Snapshot.Text.Value + "void Run() { }\n"), 1)],
                        [], [], new(actionId), [],
                        CodeActionId: request.CodeActionId,
                        CodeActionScope: request.CodeActionScope);
                },
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            TextEditor editor = workbench.ActiveSourceEditor!;
            string original = editor.Text;

            WorkbenchCodeActionCandidate candidate = new(
                new(actionId), WorkbenchClosedCodeActionKind.ImplementInterface,
                WorkbenchCodeActionScope.Occurrence, new("Implement interface"),
                new("CS0535"), new(new(0, 0), new(0, 7)));
            workbench.ApplyActiveCodeActionAsync(candidate).AsTask().GetAwaiter().GetResult();

            Assert.Equal(WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                observed?.Kind);
            Assert.Equal(actionId, observed?.CodeActionId?.Value);
            Assert.Equal(WorkbenchCodeActionScope.Occurrence, observed?.CodeActionScope);
            Assert.Contains("void Run()", editor.Text, StringComparison.Ordinal);
            editor.Document.UndoStack.Undo();
            Assert.Equal(original, editor.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_routes_a_cross_document_code_action_through_atomic_goal_mutation()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            const string actionId =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            MutationService mutations = new();
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [
                        new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new(request.Snapshot.Text.Value + "// transformed\n"), 1),
                        new(new("src/Other.cs"), request.Snapshot.BaselineHash,
                            new("class Other { }\n"),
                            new("class Other { void Changed() { } }\n"), 1),
                    ],
                    [],
                    [],
                    new(actionId),
                    [],
                    CodeActionId: request.CodeActionId,
                    CodeActionScope: request.CodeActionScope),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence,
                mutationService: mutations);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            workbench.ApplyActiveCodeActionAsync(new(
                new(actionId),
                WorkbenchClosedCodeActionKind.ReplaceMemberKind,
                WorkbenchCodeActionScope.Occurrence,
                new("Replace property with methods"),
                DiagnosticId: null,
                new(new(0, 0), new(0, 7)))).AsTask().GetAwaiter().GetResult();

            Assert.Equal(1, mutations.DocumentApplyCallCount);
            Assert.Equal(WorkbenchCodeDocumentTransformationKind.ApplyCodeAction,
                mutations.DocumentApplyRequest?.PreviewRequest.Kind);
            Assert.Contains("// transformed", workbench.ActiveSourceEditor?.Text,
                StringComparison.Ordinal);
            Assert.False(workbench.ActiveSourceDocumentIsDirty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_blocks_cross_document_action_when_an_affected_open_file_is_dirty()
    {
        using HeadlessUnitTestSession testSession =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await testSession.Dispatch(() =>
        {
            const string actionId =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            MutationService mutations = new();
            CodeIntelligenceService codeIntelligence = new()
            {
                DocumentTransformations = request => new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeTransformationDisposition.Ready,
                    request.Kind,
                    request.Range,
                    [
                        new(request.Snapshot.Path, request.Snapshot.BaselineHash,
                            request.Snapshot.Text,
                            new(request.Snapshot.Text.Value + "// transformed\n"), 1),
                        new(new("src/Other.cs"), request.Snapshot.BaselineHash,
                            new("namespace Example;"),
                            new("namespace Changed;"), 1),
                    ],
                    [], [], new(actionId), [],
                    CodeActionId: request.CodeActionId,
                    CodeActionScope: request.CodeActionScope),
            };
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(), new(), new() { Editable = true },
                codeIntelligence: codeIntelligence,
                mutationService: mutations);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/Other.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "namespace Unsaved;";
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            string activeBefore = workbench.ActiveSourceEditor!.Text;

            workbench.ApplyActiveCodeActionAsync(new(
                new(actionId), WorkbenchClosedCodeActionKind.ReplaceMemberKind,
                WorkbenchCodeActionScope.Occurrence,
                new("Replace property with methods"), DiagnosticId: null,
                new(new(0, 0), new(0, 7)), AffectedFileCount: 2))
                .AsTask().GetAwaiter().GetResult();

            Assert.Equal(0, mutations.DocumentApplyCallCount);
            Assert.Equal(activeBefore, workbench.ActiveSourceEditor.Text);
            Control sourceContent = Assert.IsAssignableFrom<Control>(
                workbench.Documents.ActiveDockable?.Context);
            Assert.Contains(sourceContent.GetLogicalDescendants().OfType<TextBlock>(), block =>
                block.Text?.Contains("Save or revert unsaved changes in src/Other.cs",
                    StringComparison.Ordinal) is true);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Floating_tools_use_the_originating_dock_window_as_owner()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(TrustedShell(), new());
            Window owner = new() { Content = workbench.Control };
            owner.Show();
            IDockable git = Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool);

            workbench.Factory.FloatDockable(git);

            IDockWindow floating = Assert.Single(workbench.Root.Windows!);
            Assert.Equal(DockWindowOwnerMode.DockableWindow, floating.OwnerMode);
            Assert.False(floating.ShowInTaskbar);
            IToolDock floatingDock = Assert.IsAssignableFrom<IToolDock>(floating.Layout?.ActiveDockable);
            Assert.Same(git, floatingDock.ActiveDockable);
            owner.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_renders_at_two_hundred_percent_without_changing_logical_layout()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Size logicalSize = window.ClientSize;
            using Bitmap normal = Assert.IsAssignableFrom<Bitmap>(
                window.CaptureRenderedFrame());

            window.SetRenderScaling(2.0);
            Dispatcher.UIThread.RunJobs();
            using Bitmap highDpi = Assert.IsAssignableFrom<Bitmap>(
                window.CaptureRenderedFrame());

            Assert.Equal(logicalSize, window.ClientSize);
            Assert.Equal(normal.PixelSize.Width * 2, highDpi.PixelSize.Width);
            Assert.Equal(normal.PixelSize.Height * 2, highDpi.PixelSize.Height);
            Assert.Equal(logicalSize.Width, workbench.Control.Bounds.Width);
            Assert.Equal(logicalSize.Height, workbench.Control.Bounds.Height);
            IToolDock left = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Right);
            IToolDock bottom = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Bottom);
            Assert.True(left.IsExpanded);
            Assert.True(right.IsExpanded);
            Assert.True(bottom.IsExpanded);
            Assert.False(left.IsEmpty);
            Assert.False(right.IsEmpty);
            Assert.False(bottom.IsEmpty);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_actions_and_editors_have_explicit_accessible_names()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true });
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Control[] contexts =
            [
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.FilesTool).Context),
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.GitTool).Context),
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.ContextTool).Context),
                Assert.IsAssignableFrom<Control>(
                    Find<IDockable>(workbench.Root, WorkbenchDockIds.RunOutputTool).Context),
                workbench.LayoutActions,
                Assert.IsAssignableFrom<Control>(workbench.Documents.ActiveDockable?.Context),
            ];
            Control[] interactive = contexts
                .SelectMany(context => context.GetVisualDescendants().OfType<Control>())
                .Where(item => item is Button or TextBox or ListBox or TextEditor)
                .ToArray();

            Assert.NotEmpty(interactive);
            Assert.All(interactive, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)),
                $"{item.GetType().Name} has no explicit accessible name."));

            Button[] chromeButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(item => item.Name is "PART_MenuButton" or
                                           "PART_PinButton" or
                                           "PART_MaximizeRestoreButton" or
                                           "PART_CloseButton")
                .ToArray();
            Assert.NotEmpty(chromeButtons);
            Assert.All(chromeButtons, item => Assert.DoesNotContain(
                "Viewbox",
                AutomationProperties.GetName(item) ?? string.Empty,
                StringComparison.Ordinal));
            Assert.All(chromeButtons, item => Assert.False(
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)),
                $"Dock chrome button {item.Name} has no accessible name."));

            ToolChromeControl[] chrome = window.GetVisualDescendants()
                .OfType<ToolChromeControl>()
                .ToArray();
            Assert.NotEmpty(chrome);
            Assert.All(chrome, item => Assert.EndsWith(
                " panel controls",
                AutomationProperties.GetName(item),
                StringComparison.Ordinal));

            Control[] splitters = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(item => item.GetType().Name == "ProportionalStackPanelSplitter")
                .ToArray();
            Assert.NotEmpty(splitters);
            Assert.All(splitters, item => Assert.Equal(
                "Resize adjacent workbench panels",
                AutomationProperties.GetName(item)));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Closed_conversation_panel_can_be_restored_and_activated()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            IDockable conversation = Find<IDockable>(
                workbench.Root, WorkbenchDockIds.ConversationTool);
            IToolDock bottom = Find<IToolDock>(workbench.Root, WorkbenchDockIds.Bottom);

            workbench.Factory.CloseDockable(conversation);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(bottom.VisibleDockables ?? [], item =>
                item.Id == WorkbenchDockIds.ConversationTool);

            Assert.True(workbench.ShowConversation());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(bottom.VisibleDockables ?? [], item =>
                item.Id == WorkbenchDockIds.ConversationTool);
            Assert.Equal(WorkbenchDockIds.ConversationTool, bottom.ActiveDockable?.Id);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Conversation_moved_to_document_region_survives_layout_restart()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            LayoutService layouts = new();
            WorkbenchDockHost first = CreateWorkbench(
                shell,
                layouts,
                conversation: ConversationSurface("First conversation"));
            Window firstWindow = new() { Width = 1280, Height = 800, Content = first.Control };
            firstWindow.Show();
            IDockable conversation = Find<IDockable>(
                first.Root, WorkbenchDockIds.ConversationTool);
            IToolDock bottom = Find<IToolDock>(first.Root, WorkbenchDockIds.Bottom);
            bottom.VisibleDockables!.Remove(conversation);
            first.Factory.AddDockable(first.Documents, conversation);
            first.Factory.SetActiveDockable(conversation);
            first.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            firstWindow.Close();

            Control restoredConversationSurface = ConversationSurface("Restored conversation");
            WorkbenchDockHost restored = CreateWorkbench(
                shell,
                layouts,
                conversation: restoredConversationSurface);
            Window restoredWindow = new()
            {
                Width = 1280,
                Height = 800,
                Content = restored.Control,
            };
            restoredWindow.Show();
            restored.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            IDockable restoredConversation = Find<IDockable>(
                restored.Root, WorkbenchDockIds.ConversationTool);
            Assert.Same(restored.Documents, restoredConversation.Owner);
            Assert.Contains(restored.Documents.VisibleDockables ?? [], item =>
                item.Id == WorkbenchDockIds.ConversationTool);
            Assert.Equal(
                WorkbenchDockIds.ConversationTool,
                restored.Documents.ActiveDockable?.Id);
            Assert.Contains(
                restoredConversationSurface,
                restoredWindow.GetVisualDescendants());
            restoredWindow.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Accessibility_tree_neutralizes_only_visual_implementation_containers()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(ApprovedGoalShell(), new());
            Window window = new() { Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Control[] implementationContainers = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(item => string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)))
                .Where(item =>
                {
                    AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(item);
                    return !peer.IsControlElement() && !peer.IsContentElement();
                })
                .ToArray();
            Assert.NotEmpty(implementationContainers);
            Button semanticButton = Assert.Single(
                Assert.IsType<StackPanel>(workbench.LayoutActions).Children.OfType<Button>(),
                item => AutomationProperties.GetName(item) == "Save current panel layout");

            AccessibilityTreeSemantics.Apply(window);

            Assert.All(implementationContainers, item =>
            {
                Assert.Equal("\u2063", AutomationProperties.GetName(item));
                Assert.Equal(string.Empty, AutomationProperties.GetClassNameOverride(item));
                Assert.Equal(
                    AutomationControlType.Custom,
                    AutomationProperties.GetControlTypeOverride(item));
            });
            Assert.Equal("Save current panel layout", AutomationProperties.GetName(semanticButton));
            Assert.Null(AutomationProperties.GetClassNameOverride(semanticButton));
            Assert.Null(AutomationProperties.GetControlTypeOverride(semanticButton));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Layout_reset_cannot_drop_a_dirty_source_buffer()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = ApprovedGoalShell();
            LayoutService layouts = new();
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                shell,
                layouts,
                new() { Editable = true },
                prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.False(layouts.WasReset);
            Assert.Contains("cancelled", workbench.LayoutStatusText, StringComparison.OrdinalIgnoreCase);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(0, workbench.SourceDocumentCount);
            Assert.True(layouts.WasReset);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workspace_switch_is_blocked_until_dirty_documents_are_resolved()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            DocumentPrompt prompt = new();
            WorkbenchDockHost workbench = CreateWorkbench(
                ApprovedGoalShell(),
                new(),
                new() { Editable = true },
                prompt);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();
            workbench.ActiveSourceEditor!.Text = "unsaved workspace-specific content";

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Cancel);
            bool cancelled = workbench.PrepareForWorkspaceChangeAsync()
                .AsTask().GetAwaiter().GetResult();

            Assert.False(cancelled);
            Assert.Equal(1, workbench.SourceDocumentCount);
            Assert.Equal("unsaved workspace-specific content", workbench.ActiveSourceEditor?.Text);
            Assert.Equal(WorkbenchDocumentTransition.Switch,
                Assert.Single(prompt.UnsavedPrompts).Transition);

            prompt.UnsavedDecisions.Enqueue(WorkbenchUnsavedDecision.Discard);
            bool accepted = workbench.PrepareForWorkspaceChangeAsync()
                .AsTask().GetAwaiter().GetResult();
            Assert.True(accepted);
            Assert.Equal(0, workbench.SourceDocumentCount);
            window.Close();
        }, CancellationToken.None);
    }

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

    [Fact]
    public async Task Layout_reset_keeps_durable_controls_in_the_rendered_tree()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = AvaloniaShellState.Initial with { IsLoading = false };
            TextBlock navigation = new() { Text = "Rendered workspace navigation" };
            TextBlock conversation = new() { Text = "Rendered conversation" };
            TextBlock context = new() { Text = "Rendered goal context" };
            WorkbenchDockHost workbench = new(
                new RunOutputService(),
                new InspectionService(),
                new DocumentService(),
                new CodeIntelligenceService(),
                new LayoutService(),
                new DocumentPrompt(),
                () => shell,
                navigation,
                conversation,
                context,
                CancellationToken.None);
            Window window = new() { Width = 1280, Height = 800, Content = workbench.Control };
            window.Show();
            workbench.Update(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(workbench.OverviewAction, window.GetVisualDescendants());
            Assert.Contains(navigation, window.GetVisualDescendants());

            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(workbench.OverviewAction, window.GetVisualDescendants());
            Assert.Contains(navigation, window.GetVisualDescendants());
            Assert.Contains(conversation, window.GetVisualDescendants());
            Assert.Contains(context, window.GetVisualDescendants());
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_layout_round_trips_moved_hidden_and_floating_production_panels()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            LayoutService layouts = new();
            WorkbenchDockHost first = CreateWorkbench(shell, layouts);
            Window firstWindow = new() { Content = first.Control };
            firstWindow.Show();
            StackPanel layoutActions = Assert.IsType<StackPanel>(first.LayoutActions);
            Assert.Contains(Assert.IsType<StackPanel>(first.DocumentActions).Children, item =>
                AutomationProperties.GetName(item) == "Workbench layout status");
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Save current panel layout");
            Assert.Contains(layoutActions.Children, item =>
                AutomationProperties.GetName(item) == "Reset panels to the default layout");
            first.OpenFileAsync("src/App.cs").AsTask().GetAwaiter().GetResult();

            IToolDock left = Find<IToolDock>(first.Root, WorkbenchDockIds.Left);
            IToolDock right = Find<IToolDock>(first.Root, WorkbenchDockIds.Right);
            IToolDock bottom = Find<IToolDock>(first.Root, WorkbenchDockIds.Bottom);
            IDockable navigation = Find<IDockable>(first.Root, WorkbenchDockIds.NavigationTool);
            IDockable files = Find<IDockable>(first.Root, WorkbenchDockIds.FilesTool);
            IDockable git = Find<IDockable>(first.Root, WorkbenchDockIds.GitTool);
            IDockable conversation = Find<IDockable>(
                first.Root, WorkbenchDockIds.ConversationTool);
            left.VisibleDockables!.Remove(navigation);
            right.VisibleDockables!.Add(navigation);
            left.VisibleDockables.Remove(files);
            first.Root.HiddenDockables ??= first.Factory.CreateList<IDockable>();
            first.Root.HiddenDockables.Add(files);
            bottom.VisibleDockables!.Remove(conversation);
            first.Root.HiddenDockables.Add(conversation);
            right.VisibleDockables.Remove(git);
            IToolDock floatingTools = first.Factory.CreateToolDock();
            floatingTools.Id = "dock.floating.git";
            floatingTools.VisibleDockables = first.Factory.CreateList(git);
            IRootDock floatingRoot = first.Factory.CreateRootDock();
            floatingRoot.Id = "dock.floating.root";
            floatingRoot.VisibleDockables = first.Factory.CreateList<IDockable>(floatingTools);
            IDockWindow floating = first.Factory.CreateDockWindow();
            floating.Id = "window.git";
            floating.X = 5000;
            floating.Y = -5000;
            floating.Width = 5000;
            floating.Height = 5000;
            floating.Layout = floatingRoot;
            first.Root.Windows = first.Factory.CreateList(floating);
            left.Proportion = double.NaN;
            right.Proportion = 0.37;

            first.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.NotNull(layouts.Stored);
            Assert.DoesNotContain("document.file", layouts.Stored, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace Example", layouts.Stored, StringComparison.Ordinal);
            firstWindow.Close();

            WorkbenchDockHost restored = CreateWorkbench(shell, layouts);
            Window restoredWindow = new() { Content = restored.Control };
            restoredWindow.Show();
            restored.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            IToolDock restoredRight = Find<IToolDock>(restored.Root, WorkbenchDockIds.Right);
            IToolDock restoredLeft = Find<IToolDock>(restored.Root, WorkbenchDockIds.Left);
            Assert.Contains(restoredRight.VisibleDockables!, item =>
                item.Id == WorkbenchDockIds.NavigationTool && item.Context is TextBlock);
            Assert.Contains(restored.Root.HiddenDockables!, item =>
                item.Id == WorkbenchDockIds.FilesTool && item.Context is not null);
            Assert.Contains(restored.Root.HiddenDockables!, item =>
                item.Id == WorkbenchDockIds.ConversationTool && item.Context is not null);
            Assert.True(restored.ShowConversation());
            IToolDock restoredBottom = Find<IToolDock>(restored.Root, WorkbenchDockIds.Bottom);
            Assert.Contains(restoredBottom.VisibleDockables!, item =>
                item.Id == WorkbenchDockIds.ConversationTool);
            Assert.Equal(WorkbenchDockIds.ConversationTool, restoredBottom.ActiveDockable?.Id);
            Assert.Equal(0.5, restoredLeft.Proportion);
            Assert.Equal(0.37, restoredRight.Proportion);
            Assert.Single(restored.Documents.VisibleDockables!);
            IDockWindow restoredWindowState = Assert.Single(restored.Root.Windows!);
            Assert.Equal(0, restoredWindowState.X);
            Assert.Equal(0, restoredWindowState.Y);
            Assert.Equal(1920, restoredWindowState.Width);
            Assert.Equal(1280, restoredWindowState.Height);
            Assert.Equal(7, DurableTools(restored.Root).Count);
            Assert.Equal("Layout restored", restored.LayoutStatusText);
            restoredWindow.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legacy_six_tool_layout_is_upgraded_with_the_problems_pane()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            LayoutService layouts = new();
            WorkbenchDockHost source = CreateWorkbench(shell, layouts);
            source.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            JsonNode payload = JsonNode.Parse(layouts.Stored!)!;
            RemovePane(payload, WorkbenchDockIds.ProblemsTool);
            layouts.Stored = payload.ToJsonString();

            WorkbenchDockHost restored = CreateWorkbench(shell, layouts);
            restored.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            Assert.NotNull(Find<ITool>(restored.Root, WorkbenchDockIds.ProblemsTool));
            Assert.Equal(7, DurableTools(restored.Root).Count);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Workbench_rejects_unknown_and_duplicate_layout_and_reset_restores_known_default()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            AvaloniaShellState shell = TrustedShell();
            LayoutService layouts = new();
            WorkbenchDockHost source = CreateWorkbench(shell, layouts);
            source.SaveLayoutAsync().AsTask().GetAwaiter().GetResult();
            string validLayout = layouts.Stored!;
            layouts.Stored = validLayout.Replace(
                WorkbenchDockIds.FilesTool,
                "tool.unknown",
                StringComparison.Ordinal);

            WorkbenchDockHost unknown = CreateWorkbench(shell, layouts);
            unknown.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Contains("rejected", unknown.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(7, DurableTools(unknown.Root).Count);

            layouts.Stored = validLayout.Replace("\"Version\": 2", "\"Version\": 1",
                StringComparison.Ordinal);
            WorkbenchDockHost obsolete = CreateWorkbench(shell, layouts);
            obsolete.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.Contains("rejected", obsolete.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(7, DurableTools(obsolete.Root).Count);

            layouts.Stored = validLayout.Replace(
                WorkbenchDockIds.FilesTool,
                WorkbenchDockIds.NavigationTool,
                StringComparison.Ordinal);

            WorkbenchDockHost workbench = CreateWorkbench(shell, layouts);
            Window window = new() { Content = workbench.Control };
            window.Show();
            workbench.RestoreLayoutAsync().AsTask().GetAwaiter().GetResult();

            Assert.Contains("rejected", workbench.LayoutStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(7, DurableTools(workbench.Root).Count);

            workbench.ResetLayoutAsync().AsTask().GetAwaiter().GetResult();
            Assert.True(layouts.WasReset);
            Assert.Null(layouts.Stored);
            Assert.Equal("Default layout restored", workbench.LayoutStatusText);
            Assert.Equal(2, Find<IToolDock>(workbench.Root, WorkbenchDockIds.Left)
                .VisibleDockables?.Count);
            window.Close();
        }, CancellationToken.None);
    }

    private static WorkbenchDockHost CreateWorkbench(
        AvaloniaShellState shell,
        LayoutService layouts,
        DocumentService? documents = null,
        DocumentPrompt? prompt = null,
        InspectionService? inspection = null,
        RunOutputService? runOutput = null,
        CodeIntelligenceService? codeIntelligence = null,
        Func<bool, Task>? manageWorkspace = null,
        MutationService? mutationService = null,
        Control? conversation = null,
        IDeveloperGitService? developerGit = null,
        Func<Task>? refreshWorkspaceContext = null,
        Func<string, Task>? manageWorkspaceAt = null) => new(
        runOutput ?? new RunOutputService(),
        inspection ?? new InspectionService(),
        documents ?? new DocumentService(),
        codeIntelligence ?? new CodeIntelligenceService(),
        layouts,
        prompt ?? new DocumentPrompt(),
        () => shell,
        new TextBlock { Text = "Workspace" },
        conversation ?? new TextBlock { Text = "Conversation" },
        new TextBlock { Text = "Goal context" },
        CancellationToken.None,
        manageWorkspace,
        mutationService,
        null,
        null,
        developerGit,
        refreshWorkspaceContext,
        manageWorkspaceAt);

    private static Control ConversationSurface(string text) => new Border
    {
        Child = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Children =
            {
                new ScrollViewer { Content = new TextBlock { Text = text } },
                new TextBox { [Grid.RowProperty] = 1, PlaceholderText = "Message Harness" },
            },
        },
    };

    private static AvaloniaShellState TrustedShell()
    {
        WorkspaceView workspace = new(
            "workspace-1",
            "/work/repository",
            "repository",
            "/work/repository/Harness.slnx",
            IsTrusted: true,
            IsActive: true,
            "main",
            IsDirty: true);
        return AvaloniaShellState.Initial with
        {
            Workspaces = WorkspaceManagementState.Initial with { Registered = [workspace] },
            IsLoading = false,
        };
    }

    private static AvaloniaShellState ApprovedGoalShell()
    {
        AvaloniaShellState shell = TrustedShell();
        GoalView goal = new(
            new("goal-1"),
            "workspace-1",
            "Edit source safely",
            "Change source only in the isolated worktree.",
            new(2),
            RemoteBudget: null,
            GoalState.Approved,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return shell with
        {
            Goals = GoalManagementState.Initial with
            {
                Items = [goal],
                SelectedGoalId = goal.Id,
            },
        };
    }

    private static T Find<T>(IDockable root, string id)
        where T : class, IDockable
    {
        HashSet<IDockable> visited = new(ReferenceEqualityComparer.Instance);
        Stack<IDockable> pending = new();
        pending.Push(root);
        while (pending.TryPop(out IDockable? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.Id == id)
            {
                return Assert.IsAssignableFrom<T>(current);
            }

            if (current is IDock dock)
            {
                foreach (IDockable child in dock.VisibleDockables ?? [])
                {
                    pending.Push(child);
                }
            }

            if (current is IRootDock rootDock)
            {
                foreach (IDockable child in (rootDock.HiddenDockables ?? [])
                             .Concat(rootDock.LeftPinnedDockables ?? [])
                             .Concat(rootDock.RightPinnedDockables ?? [])
                             .Concat(rootDock.TopPinnedDockables ?? [])
                             .Concat(rootDock.BottomPinnedDockables ?? []))
                {
                    pending.Push(child);
                }

                foreach (IDockWindow window in rootDock.Windows ?? [])
                {
                    if (window.Layout is not null)
                    {
                        pending.Push(window.Layout);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Dockable '{id}' was not found.");
    }

    private static IReadOnlyList<ITool> DurableTools(IRootDock root) =>
        WorkbenchDockIds.DurablePaneIds
            .Where(id => id.StartsWith("tool.", StringComparison.Ordinal))
            .Select(id => Find<ITool>(root, id))
        .ToArray();

    private static void RemovePane(JsonNode node, string id)
    {
        if (node is JsonArray array)
        {
            for (int index = array.Count - 1; index >= 0; index--)
            {
                JsonNode? child = array[index];
                if (child is JsonObject candidate &&
                    string.Equals(candidate["Id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    array.RemoveAt(index);
                }
                else if (child is not null)
                {
                    RemovePane(child, id);
                }
            }

            return;
        }

        if (node is JsonObject value)
        {
            foreach (JsonNode child in value.Select(property => property.Value).OfType<JsonNode>())
            {
                RemovePane(child, id);
            }
        }
    }

    private sealed class InspectionService : IWorkbenchInspectionService
    {
        internal List<WorkbenchWorkspaceRequest> Requests { get; } = [];
        internal string Diff { get; set; } = "first diff";
        internal string Status { get; set; } = "modified";
        internal bool IncludePatchUnit { get; set; }
        internal bool IsStaged { get; set; }
        internal bool IsUnstaged { get; set; }
        internal bool IsConflicted { get; set; }
        internal string IndexStatus { get; set; } = "Unaltered";
        internal string WorktreeStatus { get; set; } = "ModifiedInWorkdir";

        public ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchFileCatalogResult(
                Context(request),
                new(
                    [new("src/App.cs"), new("src/Feature.cs"), new("README.md")],
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
            WorkbenchWorkspaceRequest request,
            string query,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkbenchTextSearchResult(
                Context(request),
                new(
                    [new("src/App.cs", 1, "namespace Example;")],
                    1,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null)));
        }

        public ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            WorkbenchWorkspaceContext context = Context(request);
            return ValueTask.FromResult(new WorkbenchGitInspectionResult(
                context,
                new(
                    context.Branch?.Value ?? "main",
                    "abc123",
                    [new("src/App.cs", Status, IndexStatus, WorktreeStatus,
                        IsStaged, IsUnstaged, IsConflicted)],
                    Diff,
                    IsTruncated: false,
                    ErrorCode: null,
                    Error: null,
                    Fingerprint: "git-fingerprint",
                    PatchUnits: IncludePatchUnit
                        ? [new("patch-unit", new("src/App.cs"), DeveloperGitIndexAction.Stage,
                            DeveloperGitPatchKind.Hunk, "@@ -1 +1 @@", 1, 1, "-old +new")]
                        : [])));
        }

        private static WorkbenchWorkspaceContext Context(WorkbenchWorkspaceRequest request) =>
            request.GoalId is null
                ? new(
                    request.WorkspaceId,
                    null,
                    new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace")
                : new(
                    request.WorkspaceId,
                    request.GoalId,
                    new("harness/goal-1"),
                    WorkbenchWorkspaceScope.ApprovedGoalWorktree,
                    "Approved goal worktree · harness/goal-1");
    }

    private sealed class DeveloperGitService : IDeveloperGitService
    {
        internal DeveloperGitIndexCommand? Command { get; private set; }
        internal DeveloperGitPatchCommand? PatchCommand { get; private set; }
        internal DeveloperGitDestructivePreviewCommand? DestructivePreviewCommand { get; private set; }
        internal DeveloperGitDestructivePreviewView? AppliedDestructivePreview { get; private set; }
        internal DeveloperGitCommitPreviewCommand? CommitPreviewCommand { get; private set; }
        internal DeveloperGitCommitPreviewView? AppliedCommitPreview { get; private set; }
        internal DeveloperGitBranchCommand? BranchCommand { get; private set; }
        internal DeveloperGitBranchDeletePreviewCommand? BranchDeleteCommand { get; private set; }
        internal DeveloperGitBranchDeletePreviewView? AppliedBranchDelete { get; private set; }
        internal DeveloperGitTagCreateCommand? TagCreateCommand { get; private set; }
        internal DeveloperGitTagDeletePreviewCommand? TagDeleteCommand { get; private set; }
        internal DeveloperGitTagDeletePreviewView? AppliedTagDelete { get; private set; }
        internal DeveloperGitWorktreeCreateCommand? WorktreeCreateCommand { get; private set; }
        internal DeveloperGitWorktreeRemovePreviewCommand? WorktreeRemoveCommand { get; private set; }
        internal DeveloperGitWorktreeRemovePreviewView? AppliedWorktreeRemove { get; private set; }
        internal DeveloperGitStashCreateCommand? StashCreateCommand { get; private set; }
        internal DeveloperGitStashApplyCommand? StashApplyCommand { get; private set; }
        internal DeveloperGitStashDropPreviewCommand? StashDropCommand { get; private set; }
        internal DeveloperGitStashDropPreviewView? AppliedStashDrop { get; private set; }
        internal DeveloperGitConflictSaveCommand? ConflictSaveCommand { get; private set; }
        internal DeveloperGitConflictStageCommand? ConflictStageCommand { get; private set; }

        public ValueTask<DeveloperGitIndexCommandResult> UpdateIndexAsync(
            DeveloperGitIndexCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                new(command.Workspace.WorkspaceId, command.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                null,
                command.Paths,
                null,
                null));
        }

        public ValueTask<DeveloperGitIndexCommandResult> ApplyPatchAsync(
            DeveloperGitPatchCommand command,
            CancellationToken cancellationToken = default)
        {
            PatchCommand = command;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                new(command.Workspace.WorkspaceId, command.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                null,
                [],
                null,
                null));
        }

        public ValueTask<DeveloperGitDestructivePreviewResult> PreviewDestructiveAsync(
            DeveloperGitDestructivePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            DestructivePreviewCommand = command;
            var context = new WorkbenchWorkspaceContext(
                command.Workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitDestructivePreviewResult(
                new(
                    new("preview-id"),
                    context,
                    command.ExpectedFingerprint,
                    command.Action,
                    command.Paths,
                    "Exact destructive preview",
                    "Exact consequence.",
                    "Git does not guarantee recovery.",
                    HasGuaranteedRecovery: false),
                null,
                null,
                null));
        }

        public ValueTask<DeveloperGitIndexCommandResult> ApplyDestructiveAsync(
            DeveloperGitDestructivePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedDestructivePreview = preview;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                preview.Context, null, preview.Paths, null, null));
        }

        public ValueTask<DeveloperGitCommitPreviewResult> PreviewCommitAsync(
            DeveloperGitCommitPreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            CommitPreviewCommand = command;
            var context = new WorkbenchWorkspaceContext(
                command.Workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitCommitPreviewResult(new(
                new("commit-preview"), context, command.ExpectedFingerprint,
                command.Action, command.HookPolicy, command.Message, "main", new string('a', 40),
                "Harness Developer", "developer@harness.local", [new("src/App.cs")], "staged diff",
                "A commit will be created.", "It remains in Git history.", false),
                null, null, null));
        }

        public ValueTask<DeveloperGitCommitCommandResult> CommitAsync(
            DeveloperGitCommitPreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedCommitPreview = preview;
            return ValueTask.FromResult(new DeveloperGitCommitCommandResult(
                preview.Context, null, new string('c', 40), null, null));
        }

        public ValueTask<DeveloperGitBranchInspectionResult> InspectBranchesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitBranchInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new(new("main"), new string('a', 40), true, true),
                 new(new("feature"), new string('b', 40), false, false)], null, null));
        }

        public ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchAsync(
            DeveloperGitBranchCommand command,
            CancellationToken cancellationToken = default)
        {
            BranchCommand = command;
            return InspectBranchesAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitBranchDeletePreviewResult> PreviewBranchDeleteAsync(
            DeveloperGitBranchDeletePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            BranchDeleteCommand = command;
            DeveloperGitBranchInspectionResult inspection = await InspectBranchesAsync(
                command.Workspace, cancellationToken);
            DeveloperGitBranchView branch = inspection.Branches.Single(item => item.Name == command.Name);
            return new(new(new("delete-preview"), inspection.Context, command.ExpectedFingerprint,
                branch, command.Force, "Delete branch.", "Recovery is not guaranteed.", false),
                inspection, null, null);
        }

        public ValueTask<DeveloperGitBranchInspectionResult> ApplyBranchDeleteAsync(
            DeveloperGitBranchDeletePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedBranchDelete = preview;
            return InspectBranchesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitTagInspectionResult> InspectTagsAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitTagInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new(new("v1.0"), new string('a', 40), true, "Release", false)], null, null));
        }

        public ValueTask<DeveloperGitTagInspectionResult> CreateTagAsync(
            DeveloperGitTagCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            TagCreateCommand = command;
            return InspectTagsAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitTagDeletePreviewResult> PreviewTagDeleteAsync(
            DeveloperGitTagDeletePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            TagDeleteCommand = command;
            DeveloperGitTagInspectionResult inspection = await InspectTagsAsync(
                command.Workspace, cancellationToken);
            DeveloperGitTagView tag = inspection.Tags.Single(item => item.Name == command.Name);
            return new(new(new("tag-delete-preview"), inspection.Context, command.ExpectedFingerprint,
                tag, "Delete tag.", "Recovery is not guaranteed.", false), inspection, null, null);
        }

        public ValueTask<DeveloperGitTagInspectionResult> ApplyTagDeleteAsync(
            DeveloperGitTagDeletePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedTagDelete = preview;
            return InspectTagsAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitWorktreeInspectionResult> InspectWorktreesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitWorktreeInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                new("worktree-fingerprint"),
                [
                    new(new("/work/repository"), new("main"), new string('a', 40), true,
                        false, null, false, false, false, true, new("main-worktree-state")),
                    new(new("/work/feature"), new("feature"), new string('b', 40), false,
                        false, null, false, false, false, false, new("feature-worktree-state")),
                ], null, null));
        }

        public ValueTask<DeveloperGitWorktreeInspectionResult> CreateWorktreeAsync(
            DeveloperGitWorktreeCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            WorktreeCreateCommand = command;
            return InspectWorktreesAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitWorktreeRemovePreviewResult> PreviewWorktreeRemoveAsync(
            DeveloperGitWorktreeRemovePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            WorktreeRemoveCommand = command;
            DeveloperGitWorktreeInspectionResult inspection = await InspectWorktreesAsync(
                command.Workspace, cancellationToken);
            DeveloperGitWorktreeView worktree = inspection.Worktrees.Single(item => item.Path == command.Path);
            return new(new(new("worktree-remove-preview"), inspection.Context,
                command.ExpectedFingerprint, command.ExpectedWorktreeFingerprint, worktree,
                command.Force, "Remove worktree.", "The branch remains.", true),
                inspection, null, null);
        }

        public ValueTask<DeveloperGitWorktreeInspectionResult> ApplyWorktreeRemoveAsync(
            DeveloperGitWorktreeRemovePreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedWorktreeRemove = preview;
            return InspectWorktreesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitStashInspectionResult> InspectStashesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitStashInspectionResult(
                context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new("stash@{0}", new(new string('c', 40)), new string('a', 40),
                    DateTimeOffset.UnixEpoch, "On main: checkpoint", false)],
                null, null, null));
        }

        public ValueTask<DeveloperGitStashInspectionResult> CreateStashAsync(
            DeveloperGitStashCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            StashCreateCommand = command;
            return InspectStashesAsync(command.Workspace, cancellationToken);
        }

        public ValueTask<DeveloperGitStashInspectionResult> ApplyStashAsync(
            DeveloperGitStashApplyCommand command,
            CancellationToken cancellationToken = default)
        {
            StashApplyCommand = command;
            return InspectStashesAsync(command.Workspace, cancellationToken);
        }

        public async ValueTask<DeveloperGitStashDropPreviewResult> PreviewStashDropAsync(
            DeveloperGitStashDropPreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            StashDropCommand = command;
            DeveloperGitStashInspectionResult inspection = await InspectStashesAsync(
                command.Workspace, cancellationToken);
            DeveloperGitStashView stash = inspection.Stashes.Single(item => item.CommitSha == command.Stash);
            return new(new(new("stash-drop-preview"), inspection.Context,
                command.ExpectedFingerprint, stash, "Drop stash.", "Recovery is not guaranteed.", false),
                inspection, null, null);
        }

        public ValueTask<DeveloperGitStashInspectionResult> ApplyStashDropAsync(
            DeveloperGitStashDropPreviewView preview,
            CancellationToken cancellationToken = default)
        {
            AppliedStashDrop = preview;
            return InspectStashesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);
        }

        public ValueTask<DeveloperGitHistoryPageView> InspectHistoryAsync(
            DeveloperGitHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(request.Workspace.WorkspaceId,
                request.Workspace.GoalId, new("main"), WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace");
            return ValueTask.FromResult(new DeveloperGitHistoryPageView(context, null, request.Path,
                [new(new(new string('a', 40)), [], "Developer", DateTimeOffset.UnixEpoch,
                    "Initial", ["main"])], null, null, null));
        }

        public ValueTask<DeveloperGitCommitDetailResult> InspectCommitAsync(
            WorkbenchWorkspaceRequest workspace,
            DeveloperGitCommitSha commit,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, workspace.GoalId,
                new("main"), WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitCommitDetailResult(context, null,
                new(commit, [], "Developer", "developer@harness.local", DateTimeOffset.UnixEpoch,
                    "Developer", "developer@harness.local", DateTimeOffset.UnixEpoch,
                    "Initial", false, ["main"], [new(null, [new("README.md")], "patch", false)]),
                null, null));
        }

        public ValueTask<DeveloperGitBlamePageView> InspectBlameAsync(
            DeveloperGitBlameRequest request,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(request.Workspace.WorkspaceId,
                request.Workspace.GoalId, new("main"), WorkbenchWorkspaceScope.OriginalWorkspace,
                "Original workspace");
            return ValueTask.FromResult(new DeveloperGitBlamePageView(context, null, request.Path,
                [new(1, new(new string('a', 40)), "Developer", DateTimeOffset.UnixEpoch,
                    request.Path, 1, "line")], null, null, null));
        }

        public ValueTask<DeveloperGitConflictInspectionResult> InspectConflictsAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, workspace.GoalId,
                new("main"), WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitConflictInspectionResult(context,
                ConflictState(),
                [new(new("first.cs"), new(new string('a', 40)), new(new string('b', 40)),
                    new(new string('c', 40)), false)],
                false, null, null));
        }

        public ValueTask<DeveloperGitConflictDocumentResult> InspectConflictAsync(
            WorkbenchWorkspaceRequest workspace,
            DeveloperGitPath path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ConflictDocument(workspace, path,
                "<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n"));

        public ValueTask<DeveloperGitConflictDocumentResult> SaveConflictResultAsync(
            DeveloperGitConflictSaveCommand command,
            CancellationToken cancellationToken = default)
        {
            ConflictSaveCommand = command;
            return ValueTask.FromResult(ConflictDocument(
                command.Workspace, command.Path, command.Result));
        }

        public ValueTask<DeveloperGitIndexCommandResult> StageConflictResultAsync(
            DeveloperGitConflictStageCommand command,
            CancellationToken cancellationToken = default)
        {
            ConflictStageCommand = command;
            return ValueTask.FromResult(new DeveloperGitIndexCommandResult(
                new(command.Workspace.WorkspaceId, command.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace"),
                ConflictState(), [command.Path], null, null));
        }

        public ValueTask<DeveloperGitRemoteInspectionResult> InspectRemotesAsync(
            WorkbenchWorkspaceRequest workspace,
            CancellationToken cancellationToken = default)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, null, new("main"),
                WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return ValueTask.FromResult(new DeveloperGitRemoteInspectionResult(context,
                new("main", new string('a', 40), [], "", false, null, null, "git-fingerprint"),
                [new(new("origin"), "https://example.test/repository.git", [], [])],
                new("main"), new("origin"), new("main"), new string('a', 40),
                new string('a', 40), 0, 0, null, null));
        }

        public async ValueTask<DeveloperGitRemotePreviewResult> PreviewRemoteAsync(
            DeveloperGitRemotePreviewCommand command,
            CancellationToken cancellationToken = default)
        {
            DeveloperGitRemoteInspectionResult inspection = await InspectRemotesAsync(
                command.Workspace, cancellationToken);
            return new(new(new("remote-preview"), inspection.Context, command.ExpectedFingerprint,
                command.Action, command.Remote, command.Source, command.Destination,
                inspection.LocalSha, inspection.RemoteTrackingSha, command.PushPolicy,
                inspection.Ahead, inspection.Behind, "Synchronize refs.",
                "Configured Git helper.", "Recovery is not guaranteed."), inspection, null, null);
        }

        public ValueTask<DeveloperGitRemoteInspectionResult> ApplyRemoteAsync(
            DeveloperGitRemotePreviewView preview,
            CancellationToken cancellationToken = default) =>
            InspectRemotesAsync(new(preview.Context.WorkspaceId, null), cancellationToken);

        private static DeveloperGitConflictDocumentResult ConflictDocument(
            WorkbenchWorkspaceRequest workspace,
            DeveloperGitPath path,
            string result)
        {
            var context = new WorkbenchWorkspaceContext(workspace.WorkspaceId, workspace.GoalId,
                new("main"), WorkbenchWorkspaceScope.OriginalWorkspace, "Original workspace");
            return new(context, ConflictState(), new(path,
                new(path, new(new string('a', 40)), "base", false, false, false),
                new(path, new(new string('b', 40)), "ours", false, false, false),
                new(path, new(new string('c', 40)), "theirs", false, false, false),
                result, new(new string('d', 64)), false,
                result.Contains("<<<<<<<", StringComparison.Ordinal)
                    ? [new(1, 3, 5, "ours", "theirs", true)] : []), null, null);
        }

        private static WorkspaceGitStateView ConflictState() => new(
            "main", new string('a', 40),
            [new("first.cs", "Conflicted", "Conflicted", "Conflicted", false, true, true)],
            string.Empty, false, null, null, "conflict-state");
    }

    private sealed class RunOutputService : IRunOutputService
    {
        internal RunOutputSnapshot Result { get; set; } = new([], false, null, null);
        internal List<GoalId> Requests { get; } = [];

        public ValueTask<RunOutputSnapshot> ListAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(goalId);
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class DocumentService : IWorkbenchDocumentService
    {
        internal bool Editable { get; init; } = true;
        internal string Content { get; set; } = "namespace Example;";
        internal List<WorkbenchDocumentSaveRequest> SaveRequests { get; } = [];
        internal Queue<WorkbenchDocumentSaveResult> SaveResults { get; } = [];

        public ValueTask<WorkbenchDocumentView> OpenAsync(
            WorkbenchDocumentOpenRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchDocumentView(
                request.WorkspaceId,
                Editable ? request.GoalId : null,
                Editable && request.GoalId is not null ? new("harness/goal-1") : null,
                request.Path,
                new(Content),
                new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                new(Content.Length),
                IsTruncated: false,
                Editable ? WorkbenchDocumentAccess.Editable : WorkbenchDocumentAccess.ReadOnly,
                Editable
                    ? request.GoalId is null
                        ? "Editing the active trusted workspace."
                        : "Editing isolated branch harness/goal-1."
                    : "Read-only source.",
                ErrorCode: null,
                Error: null));

        public ValueTask<WorkbenchDocumentSaveResult> SaveAsync(
            WorkbenchDocumentSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return ValueTask.FromResult(SaveResults.TryDequeue(out WorkbenchDocumentSaveResult? result)
                ? result
                : new WorkbenchDocumentSaveResult(
                    request.WorkspaceId,
                    request.GoalId,
                    request.CorrelationId,
                    request.Path,
                    request.ExpectedSha256,
                    request.ExpectedSha256,
                    new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                    new(request.Content.Value.Length),
                    WorkbenchDocumentSaveOutcome.Saved,
                    ErrorCode: null,
                    Error: null));
        }
    }

    private sealed class DocumentPrompt : IWorkbenchDocumentPrompt
    {
        internal Queue<WorkbenchUnsavedDecision> UnsavedDecisions { get; } = [];
        internal Queue<WorkbenchConflictDecision> ConflictDecisions { get; } = [];
        internal List<WorkbenchUnsavedPrompt> UnsavedPrompts { get; } = [];
        internal List<WorkbenchConflictPrompt> ConflictPrompts { get; } = [];
        internal Queue<bool> GitDestructiveDecisions { get; } = [];
        internal List<DeveloperGitDestructivePreviewView> GitDestructivePreviews { get; } = [];
        internal Queue<DeveloperGitCommitDraft?> GitCommitDrafts { get; } = [];
        internal Queue<bool> GitCommitDecisions { get; } = [];
        internal List<DeveloperGitCommitPreviewView> GitCommitPreviews { get; } = [];
        internal Queue<bool> GitBranchDeleteDecisions { get; } = [];
        internal List<DeveloperGitBranchDeletePreviewView> GitBranchDeletePreviews { get; } = [];
        internal Queue<bool> GitTagDeleteDecisions { get; } = [];
        internal List<DeveloperGitTagDeletePreviewView> GitTagDeletePreviews { get; } = [];
        internal Queue<bool> GitWorktreeRemoveDecisions { get; } = [];
        internal List<DeveloperGitWorktreeRemovePreviewView> GitWorktreeRemovePreviews { get; } = [];
        internal Queue<bool> GitStashDropDecisions { get; } = [];
        internal List<DeveloperGitStashDropPreviewView> GitStashDropPreviews { get; } = [];

        public ValueTask<WorkbenchUnsavedDecision> DecideUnsavedAsync(
            WorkbenchUnsavedPrompt prompt,
            Window? owner)
        {
            UnsavedPrompts.Add(prompt);
            return ValueTask.FromResult(UnsavedDecisions.TryDequeue(out WorkbenchUnsavedDecision decision)
                ? decision
                : WorkbenchUnsavedDecision.Cancel);
        }

        public ValueTask<WorkbenchConflictDecision> DecideConflictAsync(
            WorkbenchConflictPrompt prompt,
            Window? owner)
        {
            ConflictPrompts.Add(prompt);
            return ValueTask.FromResult(ConflictDecisions.TryDequeue(out WorkbenchConflictDecision decision)
                ? decision
                : WorkbenchConflictDecision.Cancel);
        }

        public ValueTask<bool> ConfirmGitDestructiveAsync(
            DeveloperGitDestructivePreviewView preview,
            Window? owner)
        {
            GitDestructivePreviews.Add(preview);
            return ValueTask.FromResult(GitDestructiveDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<DeveloperGitCommitDraft?> CollectGitCommitAsync(Window? owner) =>
            ValueTask.FromResult(GitCommitDrafts.TryDequeue(out DeveloperGitCommitDraft? draft)
                ? draft : null);

        public ValueTask<bool> ConfirmGitCommitAsync(
            DeveloperGitCommitPreviewView preview,
            Window? owner)
        {
            GitCommitPreviews.Add(preview);
            return ValueTask.FromResult(GitCommitDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitBranchDeleteAsync(
            DeveloperGitBranchDeletePreviewView preview,
            Window? owner)
        {
            GitBranchDeletePreviews.Add(preview);
            return ValueTask.FromResult(GitBranchDeleteDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitTagDeleteAsync(
            DeveloperGitTagDeletePreviewView preview,
            Window? owner)
        {
            GitTagDeletePreviews.Add(preview);
            return ValueTask.FromResult(GitTagDeleteDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitWorktreeRemoveAsync(
            DeveloperGitWorktreeRemovePreviewView preview,
            Window? owner)
        {
            GitWorktreeRemovePreviews.Add(preview);
            return ValueTask.FromResult(GitWorktreeRemoveDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitStashDropAsync(
            DeveloperGitStashDropPreviewView preview,
            Window? owner)
        {
            GitStashDropPreviews.Add(preview);
            return ValueTask.FromResult(GitStashDropDecisions.TryDequeue(out bool decision) && decision);
        }


        public ValueTask<bool> ConfirmGitRemoteAsync(
            DeveloperGitRemotePreviewView preview,
            Window? owner) => ValueTask.FromResult(false);
    }

    private sealed class CodeIntelligenceService : IWorkbenchCodeIntelligenceService
    {
        internal Func<WorkbenchCodeDocumentSnapshot, WorkbenchCodeDiagnosticView>? Diagnostics
        {
            get;
            init;
        }

        internal List<WorkbenchCodeDocumentSnapshot> Snapshots { get; } = [];
        internal List<WorkbenchCodeSessionRequest> StartRequests { get; } = [];
        internal List<WorkbenchCodeSessionId> StoppedSessions { get; } = [];
        internal Func<WorkbenchCodeCompletionRequest, WorkbenchCodeCompletionView>? Completions
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeCompletionCommitRequest,
        WorkbenchCodeCompletionCommitView>? CompletionCommit
        { get; init; }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeQuickInfoView>? QuickInfo
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeSignatureHelpView>? Signatures
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeNavigationView>? Definition
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeNavigationView>? References
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeNavigationView>? Implementations
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeVirtualDocumentRequest, WorkbenchCodeVirtualDocumentView>?
            VirtualDocument
        { get; init; }
        internal Func<WorkbenchCodeInspectionRequest, WorkbenchCodeInspectionView>? Inspection
        { get; init; }
        internal Func<
            WorkbenchCodeDocumentPresentationRequest,
            WorkbenchCodeDocumentPresentationView>? Presentation
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeOccurrenceView>? Occurrences
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeDocumentTransformationPreviewRequest,
            WorkbenchCodeDocumentTransformationPreviewView>? DocumentTransformations
        { get; init; }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeMissingImportView>? MissingImports
        { get; init; }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeActionView>? CodeActions
        { get; init; }
        internal int ImplementationCallCount { get; private set; }

        public ValueTask<WorkbenchCodeSessionView> StartAsync(
            WorkbenchCodeSessionRequest request,
            IProgress<WorkbenchCodeLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            StartRequests.Add(request);
            return ValueTask.FromResult<WorkbenchCodeSessionView>(new(
                new("context-1"),
                new("session-1"),
                WorkbenchCodeResultState.Ready,
                []));
        }

        public ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
            WorkbenchCodeDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return ValueTask.FromResult(Diagnostics?.Invoke(snapshot) ?? new(
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [],
                []));
        }

        public ValueTask<WorkbenchCodeValidationView> ValidateAsync(
            WorkbenchCodeValidationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkbenchCodeValidationView>(new(
            request.SessionId,
            WorkbenchCodeResultState.Degraded,
            WorkbenchCodeValidationDisposition.NotApplicable,
            [],
            []));

        public ValueTask<WorkbenchCodeCompletionView> GetCompletionsAsync(
            WorkbenchCodeCompletionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Completions?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    null,
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [],
                    []));
        public ValueTask<WorkbenchCodeCompletionCommitView> CommitCompletionAsync(
            WorkbenchCodeCompletionCommitRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                CompletionCommit?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Stale,
                    [],
                    null,
                    [new(new("completion_unavailable"), new("Completion is unavailable."))]));
        public ValueTask<WorkbenchCodeQuickInfoView> GetQuickInfoAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                QuickInfo?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, null, [], []));
        public ValueTask<WorkbenchCodeSignatureHelpView> GetSignatureHelpAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Signatures?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], 0, 0, []));
        public ValueTask<WorkbenchCodeNavigationView> FindDefinitionAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Definition?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], []));
        public ValueTask<WorkbenchCodeNavigationView> FindReferencesAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                References?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], []));
        public ValueTask<WorkbenchCodeNavigationView> FindImplementationsAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ImplementationCallCount++;
            return ValueTask.FromResult(
                Implementations?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], []));
        }

        public ValueTask<WorkbenchCodeVirtualDocumentView> GetVirtualDocumentAsync(
            WorkbenchCodeVirtualDocumentRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                VirtualDocument?.Invoke(request) ?? new(
                    request.Snapshot.SessionId, request.Snapshot.Path,
                    request.Snapshot.BufferVersion, WorkbenchCodeResultState.Failed,
                    request.Id, null, null, null, null, null, true,
                    [new(new("virtual_document_unavailable"),
                        new("Virtual source is unavailable."))]));

        public ValueTask<WorkbenchCodeInspectionView> InspectAsync(
            WorkbenchCodeInspectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Inspection?.Invoke(request) ?? new(
                    request.Snapshot.SessionId, request.Snapshot.Path,
                    request.Snapshot.BufferVersion, WorkbenchCodeResultState.Failed,
                    request.Kind, null, null, null, true, false,
                    [new(new("inspection_unavailable"),
                        new("Code inspection is unavailable."))]));

        public ValueTask<WorkbenchCodeDocumentPresentationView> GetDocumentPresentationAsync(
            WorkbenchCodeDocumentPresentationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Presentation?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [], [], [], [], [], [], false, []));

        public ValueTask<WorkbenchCodeOccurrenceView> FindOccurrencesAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Occurrences?.Invoke(snapshot) ?? new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    null,
                    [],
                    false,
                    []));

        public ValueTask<WorkbenchCodeDocumentTransformationPreviewView>
            PreviewDocumentTransformationAsync(
                WorkbenchCodeDocumentTransformationPreviewRequest request,
                CancellationToken cancellationToken = default) => ValueTask.FromResult(
                DocumentTransformations?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Failed,
                    WorkbenchCodeTransformationDisposition.Rejected,
                    request.Kind,
                    request.Range,
                    Edits: [],
                    [],
                    [],
                    Fingerprint: null,
                    [new(new("document_transformation_unavailable"),
                        new("Document transformation is unavailable."))]));

        public ValueTask<WorkbenchCodeMissingImportView> GetMissingImportsAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            MissingImports?.Invoke(snapshot) ?? new(
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [],
                []));

        public ValueTask<WorkbenchCodeActionView> GetCodeActionsAsync(
            WorkbenchCodeActionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            CodeActions?.Invoke(request.Snapshot) ?? new(
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [],
                []));

        public ValueTask StopAsync(
            WorkbenchCodeSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            StoppedSessions.Add(sessionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutationService : IWorkspaceMutationService
    {
        private const string Fingerprint =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string NewHash =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        internal RenameSymbolPreviewRequest? PreviewRequest { get; private set; }
        internal int ApplyCallCount { get; private set; }
        internal DocumentTransformationApplyRequest? DocumentApplyRequest { get; private set; }
        internal int DocumentApplyCallCount { get; private set; }

        public ValueTask<RenameSymbolPreviewView> PreviewRenameAsync(
            RenameSymbolPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            PreviewRequest = request;
            return ValueTask.FromResult(new RenameSymbolPreviewView(
                Preview(request),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<RenameSymbolApplyView> ApplyRenameAsync(
            RenameSymbolApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            WorkbenchCodeRenamePreviewView preview = Preview(request.PreviewRequest);
            return ValueTask.FromResult(new RenameSymbolApplyView(
                request.PreviewRequest.GoalId,
                request.CorrelationId,
                preview,
                [new(
                    request.PreviewRequest.GoalId,
                    request.CorrelationId,
                    "src/App.cs",
                    request.PreviewRequest.BaselineHash.Value,
                    NewHash,
                    "namespace Renamed;".Length,
                    WasCreated: false,
                    ErrorCode: null,
                    Error: null)],
                WasRolledBack: false,
                WasCancelled: false,
                new(
                    new("session-1"),
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeValidationDisposition.Validated,
                    [],
                    []),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<FileEditView> ApplyFileEditAsync(
            FileEditRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DocumentTransformationApplyView> ApplyDocumentTransformationAsync(
            DocumentTransformationApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            DocumentApplyRequest = request;
            DocumentApplyCallCount++;
            DocumentTransformationPreviewRequest source = request.PreviewRequest;
            WorkbenchCodeDocumentTransformationPreviewView preview = new(
                new("session-1"),
                source.Path,
                source.BufferVersion,
                WorkbenchCodeResultState.Ready,
                WorkbenchCodeTransformationDisposition.Ready,
                source.Kind,
                source.Range,
                [
                    new(source.Path, source.BaselineHash, source.Text,
                        new(source.Text.Value + "// transformed\n"), 1),
                    new(new("src/Other.cs"), new(Fingerprint), new("class Other { }\n"),
                        new("class Other { void Changed() { } }\n"), 1),
                ],
                [],
                [],
                new(Fingerprint),
                [],
                source.ImportNamespace,
                source.FormattingTrigger,
                source.CodeActionId,
                source.CodeActionScope);
            return ValueTask.FromResult(new DocumentTransformationApplyView(
                source.GoalId,
                request.CorrelationId,
                preview,
                preview.Edits.Select(edit => new FileEditView(
                    source.GoalId,
                    request.CorrelationId,
                    edit.Path.Value,
                    edit.BaselineHash.Value,
                    NewHash,
                    edit.Text.Value.Length,
                    WasCreated: false,
                    ErrorCode: null,
                    Error: null)).ToArray(),
                WasRolledBack: false,
                WasCancelled: false,
                new(new("session-1"), WorkbenchCodeResultState.Ready,
                    WorkbenchCodeValidationDisposition.Validated, [], []),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<DotNetOperationView> RunDotNetAsync(
            DotNetOperationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static WorkbenchCodeRenamePreviewView Preview(RenameSymbolPreviewRequest request) => new(
            new("session-1"),
            request.Path,
            request.BufferVersion,
            WorkbenchCodeResultState.Ready,
            WorkbenchCodeTransformationDisposition.Ready,
            new("Class|Example"),
            request.NewName,
            [new(
                request.Path,
                request.BaselineHash,
                request.Text,
                new("namespace Renamed;"),
                1)],
            [],
            [],
            new(Fingerprint),
            []);
    }

    private sealed class McpSettingsService : IMcpSettingsService
    {
        private readonly McpSettingsSnapshot snapshot = new([
            new(
                new("docs"),
                new("https://docs.example.test/mcp"),
                new(30),
                McpConnectionKind.ReadOnly,
                ClientId: null,
                AllowedTools: [],
                IsEnabled: true,
                State: McpConnectionState.Ready,
                NegotiatedProtocolVersion: "2026-07-28",
                DiscoveredTools: 2,
                AgentEligibleTools: 1,
                RejectedTools: 1,
                Message: null,
                RequiresRestart: false),
        ]);

        public ValueTask<McpSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<McpSettingsSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<McpSettingsResult> SaveAsync(
            McpConnectionSettingsUpdate request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new McpSettingsResult(snapshot, null, null));

        public ValueTask<McpSettingsResult> DeleteAsync(
            McpConnectionName name,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new McpSettingsResult(snapshot, null, null));
    }

    private sealed class VisualCaptureService : IVisualCaptureService
    {
        private static readonly VisualCaptureSettingsSnapshot Settings = new(
            VisualCapturePreferences.Default,
            new(true, 3,
                [VisualCaptureTarget.UserSelection, VisualCaptureTarget.Window], null, null),
            "Private XDG state; excluded from repositories and backups.");

        public ValueTask<VisualCaptureSettingsSnapshot> GetSettingsAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Settings);

        public ValueTask<VisualCaptureSettingsResult> SaveSettingsAsync(
            VisualCapturePreferences preferences,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureSettingsResult(
                Settings with { Preferences = preferences }, null, null));

        public ValueTask<VisualCaptureResult> CaptureAsync(
            VisualCaptureRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureResult(
                VisualCaptureOutcome.Cancelled, null, "capture_cancelled", "Cancelled"));

        public ValueTask<VisualCaptureListResult> ListAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureListResult([], null, null));

        public ValueTask<VisualCaptureInspectionResult> InspectAsync(
            GoalId goalId,
            VisualCaptureId captureId,
            VisualCaptureModelAccess access,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VisualCaptureInspectionResult(
                VisualCaptureOutcome.NotFound, null, "capture_not_found", "Not found"));

        public ValueTask<bool> DeleteAsync(
            GoalId goalId,
            VisualCaptureId captureId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public ValueTask CleanupAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class ResearchSettingsService : IResearchSettingsService
    {
        private static readonly ResearchSettingsSnapshot Snapshot = new(
            true, true, true, true, false,
            ["/docs"], ["docs/search"], ["https://learn.microsoft.com/api/search"],
            ["https://api.nuget.org/v3/index.json"], ResearchRefreshMode.OnDemand,
            5, 12_000, 168, 30, 3, 1_024, null);

        public ValueTask<ResearchSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);

        public ValueTask<ResearchSettingsResult> SaveAsync(ResearchSettingsUpdate update,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResearchSettingsResult(Snapshot, null, null));

        public ValueTask<ResearchSettingsSnapshot> CleanupCacheAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);
    }

    private sealed class KeybindingSettingsService : IKeybindingSettingsService
    {
        private KeybindingSettingsSnapshot snapshot = KeybindingSettingsSnapshot.Default;

        public ValueTask<KeybindingSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public KeybindingValidationResult Validate(KeybindingUpdateRequest request)
        {
            string[] duplicates = request.Entries.SelectMany(entry =>
                    entry.GestureText.Split(';', StringSplitOptions.TrimEntries |
                                               StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(text => text, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            IReadOnlyList<KeybindingIssue> issues = duplicates.Select(text => new KeybindingIssue(
                KeybindingIssueKind.Conflict, null, $"{text} conflicts with another command.")).ToArray();
            return new(issues.Count == 0, issues, snapshot.Bindings);
        }

        public ValueTask<KeybindingSettingsSnapshot> SaveAsync(
            KeybindingUpdateRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<KeybindingSettingsSnapshot> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            snapshot = KeybindingSettingsSnapshot.Default;
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<string> ExportAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("{\"format\":\"harness-keybindings-v1\",\"bindings\":[]}");

        public ValueTask<KeybindingSettingsSnapshot> ImportAsync(
            string document,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);
    }

    private sealed class LayoutService : IWorkbenchLayoutService
    {
        internal string? Stored { get; set; }
        internal bool WasReset { get; private set; }

        public ValueTask<WorkbenchLayoutLoadResult> LoadAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            Stored is null
                ? new WorkbenchLayoutLoadResult(WorkbenchLayoutLoadState.Missing, null, null)
                : new WorkbenchLayoutLoadResult(
                    WorkbenchLayoutLoadState.Available,
                    new(Stored),
                    null));

        public ValueTask<WorkbenchLayoutWriteResult> SaveAsync(
            WorkbenchLayoutPayload layout,
            CancellationToken cancellationToken = default)
        {
            Stored = layout.Value;
            return ValueTask.FromResult(new WorkbenchLayoutWriteResult(true, null));
        }

        public ValueTask<WorkbenchLayoutWriteResult> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            Stored = null;
            WasReset = true;
            return ValueTask.FromResult(new WorkbenchLayoutWriteResult(true, null));
        }
    }
}
