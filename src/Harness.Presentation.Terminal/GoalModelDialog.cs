using System.Collections.ObjectModel;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Goals;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed class GoalModelDialog : Dialog
{
    private readonly IApplication application;
    private readonly IGoalModelService modelService;
    private readonly GoalView goal;
    private readonly GoalModelCatalog catalog;
    private readonly CancellationToken cancellationToken;
    private readonly ListView modelList;
    private readonly Label selectionsText;
    private readonly Label status;
    private readonly Button lead;
    private readonly Button implementer;
    private readonly Button reviewer;
    private IReadOnlyList<GoalModelSelectionView> selections;
    private IReadOnlyList<GoalModelCandidate> visibleModels;

    internal GoalModelDialog(
        IApplication application,
        IGoalModelService modelService,
        GoalView goal,
        GoalModelCatalog catalog,
        IReadOnlyList<GoalModelSelectionView> selections,
        CancellationToken cancellationToken)
    {
        this.application = application;
        this.modelService = modelService;
        this.goal = goal;
        this.catalog = catalog;
        this.selections = selections;
        visibleModels = catalog.Models;
        this.cancellationToken = cancellationToken;

        Title = "Goal role models";
        Width = Dim.Percent(95);
        Height = Dim.Percent(85);
        selectionsText = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 4,
            Text = GoalTextFormatter.FormatSelections(selections),
        };
        TextField search = new()
        {
            X = 0,
            Y = 5,
            Width = Dim.Fill(),
            Text = string.Empty,
        };
        Label searchLabel = new()
        {
            X = 0,
            Y = 4,
            Text = "Search provider/model (Enter to filter)",
        };
        modelList = new()
        {
            X = 0,
            Y = 6,
            Width = Dim.Fill(),
            Height = Dim.Fill(7),
        };
        modelList.SetSource(new ObservableCollection<string>(visibleModels
            .Select(GoalTextFormatter.FormatModelCandidate)
            .ToArray()));
        search.Accepting += (_, args) =>
        {
            args.Handled = true;
            string value = search.Text?.ToString()?.Trim() ?? string.Empty;
            visibleModels = catalog.Models.Where(candidate => value.Length == 0 ||
                $"{candidate.Provider.Value} {candidate.Model.Value} {candidate.Access}"
                    .Contains(value, StringComparison.OrdinalIgnoreCase)).ToArray();
            modelList.SetSource(new ObservableCollection<string>(visibleModels
                .Select(GoalTextFormatter.FormatModelCandidate)
                .ToArray()));
            modelList.SelectedItem = visibleModels.Count > 0 ? 0 : null;
            SetButtonsEnabled(visibleModels.Count > 0);
        };
        lead = RoleButton("_Lead", 0, AgentRole.Lead);
        implementer = RoleButton("_Implementer", Pos.Right(lead) + 1, AgentRole.Implementer);
        reviewer = RoleButton("_Reviewer", Pos.Right(implementer) + 1, AgentRole.Reviewer);
        status = new()
        {
            X = 0,
            Y = Pos.AnchorEnd(5),
            Width = Dim.Fill(),
            Height = 2,
            Text = StatusText(catalog),
        };
        SetButtonsEnabled(catalog.Models.Count > 0);
        Add(selectionsText, searchLabel, search, modelList, lead, implementer, reviewer, status);
        AddButton(new Button { Title = "_Close" });
    }

    internal IReadOnlyList<GoalModelSelectionView> Selections => selections;

    private Button RoleButton(string title, Pos x, AgentRole role)
    {
        Button button = new()
        {
            Title = title,
            X = x,
            Y = Pos.AnchorEnd(3),
        };
        button.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await SelectAsync(role);
        };
        return button;
    }

    private async Task SelectAsync(AgentRole role)
    {
        int selectedIndex = modelList.SelectedItem ?? -1;
        if (selectedIndex < 0 || selectedIndex >= visibleModels.Count)
        {
            status.Text = "Select a model.";
            return;
        }

        GoalModelCandidate candidate = visibleModels[selectedIndex];
        if (candidate.Access is ModelAccess.Remote)
        {
            if (goal.RemoteBudget is null)
            {
                status.Text = "This goal is local-only; choose unlimited or capped remote spend to authorize remote models.";
                return;
            }

            string pricing = candidate.InputPrice is null || candidate.OutputPrice is null
                ? "Published pricing is unavailable; inference will fail closed until pricing is known."
                : $"Published rates: input ${candidate.InputPrice.Value:0.######}/M tokens, " +
                  $"output ${candidate.OutputPrice.Value:0.######}/M tokens.";
            int? choice = MessageBox.Query(
                application,
                "Authorize remote model",
                $"Select {candidate.Provider.Value}/{candidate.Model.Value} for {role}?\n\n" +
                $"{GoalTextFormatter.FormatCostStatus(goal, report: null)}. {pricing}\n\n" +
                "Every request reserves a conservative maximum and remains attributed to this goal.",
                "_Authorize",
                "_Cancel");
            if (choice != 0)
            {
                return;
            }
        }

        try
        {
            SetButtonsEnabled(false);
            GoalModelSelectionResult result = await modelService.SelectAsync(new(
                goal.Id,
                role,
                candidate.Provider,
                candidate.Model), cancellationToken);
            if (result.Selection is null)
            {
                status.Text = result.Error ?? "Model selection failed.";
                return;
            }

            selections = await modelService.GetSelectionsAsync(goal.Id, cancellationToken);
            selectionsText.Text = GoalTextFormatter.FormatSelections(selections);
            status.Text = $"Selected {candidate.Provider.Value}/{candidate.Model.Value} for {role}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestStop();
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetButtonsEnabled(catalog.Models.Count > 0);
            }
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        lead.Enabled = enabled;
        implementer.Enabled = enabled;
        reviewer.Enabled = enabled;
    }

    private static string StatusText(GoalModelCatalog catalog)
    {
        if (catalog.Error is not null)
        {
            return catalog.Error;
        }

        return catalog.Issues.Count == 0
            ? $"{catalog.Models.Count} chat model(s). Remote catalog refresh does not perform inference."
            : $"{catalog.Models.Count} chat model(s); " + string.Join(" | ", catalog.Issues.Select(issue =>
                $"{issue.Provider.Value}: {issue.Message}"));
    }
}
