using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Harness.BusinessLogic.Agents;

namespace Harness.Presentation.Avalonia;

internal static class ModelSelectionCatalog
{
    internal static GoalModelCandidate[] ForRole(
        IEnumerable<GoalModelCandidate> candidates,
        AgentRole role) => candidates
        .Where(candidate => candidate.SupportedRoles.Contains(role))
        .ToArray();
}

/// <summary>
/// Shared provider/model picker. Search covers the complete supplied catalog while
/// selection remains a typed candidate instead of arbitrary user-entered text.
/// </summary>
internal sealed class SearchableModelPicker : UserControl
{
    private readonly AutoCompleteBox picker = new()
    {
        MinimumPrefixLength = 0,
        MinimumPopulateDelay = TimeSpan.Zero,
        MaxDropDownHeight = 360,
        IsTextCompletionEnabled = false,
        PlaceholderText = "Search provider or model",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    internal SearchableModelPicker()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        picker.ItemFilter = Matches;
        picker.TextSelector = static (_, item) => item?.ToString() ?? string.Empty;
        Content = picker;
    }

    internal GoalModelCandidate? SelectedCandidate =>
        (picker.SelectedItem as SearchableModelChoice)?.Candidate;

    internal event EventHandler<SelectionChangedEventArgs> SelectionChanged
    {
        add => picker.SelectionChanged += value;
        remove => picker.SelectionChanged -= value;
    }

    internal void SetAutomationName(string name)
    {
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetName(picker, name);
    }

    internal void SetCandidates(
        IEnumerable<GoalModelCandidate> candidates,
        GoalModelCandidate? preferred = null)
    {
        SearchableModelChoice[] choices = candidates
            .Select(candidate => new SearchableModelChoice(candidate))
            .ToArray();
        GoalModelCandidate? current = SelectedCandidate;
        picker.ItemsSource = choices;
        SearchableModelChoice? selected =
            Find(choices, preferred) ?? Find(choices, current) ?? choices.FirstOrDefault();
        picker.SelectedItem = selected;
        picker.Text = selected?.ToString() ?? string.Empty;
    }

    private static SearchableModelChoice? Find(
        IEnumerable<SearchableModelChoice> choices,
        GoalModelCandidate? candidate) => candidate is null
            ? null
            : choices.FirstOrDefault(choice =>
                choice.Candidate.Provider == candidate.Provider &&
                choice.Candidate.Model == candidate.Model);

    private static bool Matches(string? search, object? item) =>
        item is SearchableModelChoice choice &&
        (string.IsNullOrWhiteSpace(search) ||
         choice.SearchText.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    private sealed record SearchableModelChoice(GoalModelCandidate Candidate)
    {
        internal string SearchText =>
            $"{Candidate.Provider.Value} {Candidate.Model.Value} {Candidate.Access} " +
            string.Join(' ', Candidate.Capabilities.Select(capability => capability.Value));

        public override string ToString() => GoalPresentationFormatter.FormatCandidate(Candidate);
    }
}
