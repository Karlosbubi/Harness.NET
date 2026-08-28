using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Harness.BusinessLogic.Agents;

namespace Harness.Presentation.Avalonia;

internal static class AgentRoleDefaultCard
{
    internal static Control Create(
        AgentRoleDefault roleDefault,
        IReadOnlyList<GoalModelCandidate> candidates,
        AgentRoleDefaultIssue? defaultIssue,
        bool isBusy,
        Func<AgentRole, GoalModelCandidate, AgentReasoningPolicy, ValueTask> saveAsync)
    {
        GoalModelCandidate[] choices = ModelSelectionCatalog.ForRole(
            candidates, roleDefault.Role);
        GoalModelCandidate? selected = choices.FirstOrDefault(item =>
            item.Provider == roleDefault.Provider && item.Model == roleDefault.Model);
        SearchableModelPicker model = new()
        {
            MinWidth = 260,
            IsEnabled = !isBusy && choices.Length > 0,
            IsVisible = choices.Length > 0,
        };
        model.SetCandidates(choices, selected);
        model.SetAutomationName($"{roleDefault.Role} default model");

        ReasoningPolicyChoice[] reasoningChoices =
        [
            new(
                AgentReasoningPolicy.ProviderDefault,
                "Provider default (model decides, potentially slower)"),
            new(AgentReasoningPolicy.Disabled, "Off (faster responses)"),
        ];
        ComboBox reasoning = new()
        {
            ItemsSource = reasoningChoices,
            SelectedItem = reasoningChoices.First(choice =>
                choice.Policy == roleDefault.ReasoningPolicy),
            MinWidth = 260,
            IsEnabled = !isBusy,
        };
        AutomationProperties.SetName(
            reasoning,
            $"{roleDefault.Role} default reasoning policy");

        Button save = new()
        {
            Content = "Save default",
            IsVisible = choices.Length > 0,
        };
        save.Classes.Add("command");
        AutomationProperties.SetName(save, $"Save {roleDefault.Role} agent defaults");
        void UpdateSaveState() => save.IsEnabled =
            !isBusy && model.SelectedCandidate is not null &&
            reasoning.SelectedItem is ReasoningPolicyChoice;
        model.SelectionChanged += (_, _) => UpdateSaveState();
        reasoning.SelectionChanged += (_, _) => UpdateSaveState();
        UpdateSaveState();
        save.Click += async (_, _) =>
        {
            if (model.SelectedCandidate is { } candidate &&
                reasoning.SelectedItem is ReasoningPolicyChoice selectedReasoning)
            {
                await saveAsync(roleDefault.Role, candidate, selectedReasoning.Policy);
            }
        };

        Border unavailable = new()
        {
            Classes = { "editor-access" },
            IsVisible = choices.Length == 0,
            Child = new TextBlock
            {
                Text = "Discover available models to edit this route.",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Grid fields = new()
        {
            RowDefinitions = new("Auto,Auto,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10,
            Children = { model, unavailable },
        };
        Grid.SetRow(reasoning, 1);
        fields.Children.Add(reasoning);
        Grid.SetRow(save, 2);
        fields.Children.Add(save);

        return new Border
        {
            Classes = { "card", "row" },
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = roleDefault.Role.ToString(),
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = Status(roleDefault, defaultIssue),
                        Classes = { "muted" },
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    fields,
                },
            },
        };
    }

    private static string Status(
        AgentRoleDefault roleDefault,
        AgentRoleDefaultIssue? issue) => issue is null
        ? $"Effective: {roleDefault.Access} · {roleDefault.Provider.Value}/{roleDefault.Model.Value}" +
          $" · Reasoning {ReasoningPolicyLabel(roleDefault.ReasoningPolicy)}" +
          (roleDefault.IsPersisted ? " · Saved" : " · Host fallback")
        : $"Needs attention: {issue.Message}";

    private static string ReasoningPolicyLabel(AgentReasoningPolicy policy) => policy switch
    {
        AgentReasoningPolicy.Disabled => "off",
        AgentReasoningPolicy.ProviderDefault => "provider default",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    private sealed record ReasoningPolicyChoice(AgentReasoningPolicy Policy, string Name)
    {
        public override string ToString() => Name;
    }
}
