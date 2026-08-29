using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class GitChangesTool
{
    private readonly WorkbenchToolContext context;
    private readonly ListBox changes = new();
    private readonly ListBox patchUnits = new();
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button stage = new() { Content = "Stage" };
    private readonly Button unstage = new() { Content = "Unstage" };
    private readonly Button clearSelection = new() { Content = "Whole file" };
    private readonly Button discard = new() { Content = "Discard file" };
    private readonly Button clean = new() { Content = "Delete untracked" };
    private readonly Button commit = new() { Content = "Commit…" };
    private IReadOnlyList<DeveloperGitPatchUnitView> currentPatchUnits = [];

    internal GitChangesTool(WorkbenchToolContext context)
    {
        this.context = context;
        Actions = BuildActions();
        Content = BuildContent();
    }

    internal Control Actions { get; }
    internal Control Content { get; }
    internal ListBox Changes => changes;
    internal TextBlock Summary => summary;
    internal TextBlock Status => status;
    internal string Fingerprint { get; private set; } = string.Empty;
    internal WorkbenchWorkspaceContext? CurrentContext { get; private set; }

    internal void Reset(WorkspaceView? active, bool sourceContextChanged)
    {
        changes.ItemsSource = Array.Empty<ChangeChoice>();
        UpdatePatchUnitChoices();
        ReportStatus(string.Empty);
        summary.Text = active is null
            ? "No workspace selected."
            : sourceContextChanged
                ? "Refreshing Git state for the current source context…"
                : "Refresh Git state.";
    }

    internal void Render(WorkbenchWorkspaceContext sourceContext, WorkspaceGitStateView git)
    {
        int conflictCount = git.Changes.Count(change => change.IsConflicted ||
            change.Status.Contains("Conflicted", StringComparison.OrdinalIgnoreCase));
        Fingerprint = git.Fingerprint;
        CurrentContext = sourceContext;
        summary.Text = $"{sourceContext.Description}\nBranch {git.Branch}\n" +
                       $"HEAD {git.HeadSha ?? "unborn"}\n" +
                       $"{git.Changes.Count} change(s)" +
                       (conflictCount > 0 ? $" · {conflictCount} conflict(s)" : string.Empty) +
                       (git.IsTruncated ? " · truncated" : string.Empty);
        changes.ItemsSource = git.Changes
            .Select(change => new ChangeChoice(change, sourceContext.GoalId))
            .ToArray();
        currentPatchUnits = git.PatchUnits ?? [];
        changes.SelectedIndex = git.Changes.Count > 0 ? 0 : -1;
        UpdatePatchUnitChoices();
        ReportStatus(conflictCount > 0
            ? $"{conflictCount} unresolved Git conflict(s) block commit approval. " +
              "Use the Conflicts tab to inspect base, ours, and theirs; save the result, then stage it explicitly."
            : "Git state refreshed.");
    }

    internal void ReportStatus(string message) => status.Text = message;

    internal async ValueTask UpdateSelectedIndexAsync(DeveloperGitIndexAction action)
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null ||
            changes.SelectedItem is not ChangeChoice selected || string.IsNullOrEmpty(Fingerprint))
        {
            ReportStatus("Select a current Git change first.");
            return;
        }
        if (action is DeveloperGitIndexAction.Stage && selected.Change.IsConflicted)
        {
            ReportStatus("Use the Conflicts tab to inspect, save, and explicitly stage this merge result.");
            return;
        }

        await context.RunAsync(async () =>
        {
            DeveloperGitIndexCommandResult result;
            if (patchUnits.SelectedItem is PatchChoice patch)
            {
                if (patch.Unit.Action != action)
                {
                    ReportStatus($"That selection is for {patch.Unit.Action.ToString().ToLowerInvariant()}.");
                    return;
                }
                result = await service.ApplyPatchAsync(new(
                    context.Request(active), new(Fingerprint), patch.Unit.Id), context.CancellationToken);
            }
            else
            {
                result = await service.UpdateIndexAsync(new(
                    context.Request(active), new(Fingerprint), action,
                    [new(selected.Change.Path)]), context.CancellationToken);
            }
            if (result.State is not null) Render(result.Context, result.State);
            ReportStatus(result.ErrorCode == "git_state_stale"
                ? "Git changed outside Harness.NET. The view was refreshed; review it and retry."
                : result.Error ??
                  $"{(action == DeveloperGitIndexAction.Stage ? "Staged" : "Unstaged")} {selected.Change.Path}.");
        });
    }

    internal async ValueTask ComposeAndCommitAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            CurrentContext?.Scope != WorkbenchWorkspaceScope.OriginalWorkspace ||
            string.IsNullOrEmpty(Fingerprint))
        {
            ReportStatus("Refresh the original workspace Git state before committing.");
            return;
        }
        if (context.HasDirtyOriginalDocuments())
        {
            ReportStatus("Save or discard every unsaved original-workspace editor buffer before committing.");
            return;
        }
        DeveloperGitCommitDraft? draft = await prompt.CollectGitCommitAsync(context.OwnerWindow());
        if (draft is null)
        {
            ReportStatus("Developer Git commit cancelled; no commit was created.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitCommitPreviewResult result = await service.PreviewCommitAsync(new(
                context.Request(active), new(Fingerprint), draft.Action, draft.HookPolicy,
                draft.Message), context.CancellationToken);
            if (result.State is not null && CurrentContext is not null)
                Render(CurrentContext, result.State);
            if (result.Preview is null)
            {
                ReportStatus(result.Error ?? "The developer commit preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitCommitAsync(result.Preview, context.OwnerWindow()))
            {
                ReportStatus("Developer Git commit cancelled after preview; no commit was created.");
                return;
            }
            DeveloperGitCommitCommandResult committed = await service.CommitAsync(
                result.Preview, context.CancellationToken);
            if (committed.State is not null) Render(committed.Context, committed.State);
            ReportStatus(committed.Error ??
                $"{(draft.Action == DeveloperGitCommitAction.Amend ? "Amended" : "Created")} commit {committed.CommitSha}.");
        });
    }

    internal async ValueTask PreviewAndApplyDestructiveAsync(DeveloperGitDestructiveAction action)
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            changes.SelectedItem is not ChangeChoice selected || string.IsNullOrEmpty(Fingerprint))
        {
            ReportStatus("Select a current whole-file Git change first.");
            return;
        }
        if (patchUnits.SelectedItem is not null)
        {
            ReportStatus("Choose Whole file before a destructive Git action.");
            return;
        }
        if (context.IsOriginalDocumentDirty(selected.Change.Path))
        {
            ReportStatus($"Save or discard the unsaved editor buffer for {selected.Change.Path} first.");
            return;
        }

        await context.RunAsync(async () =>
        {
            DeveloperGitDestructivePreviewResult result = await service.PreviewDestructiveAsync(new(
                context.Request(active), new(Fingerprint), action,
                [new(selected.Change.Path)]), context.CancellationToken);
            if (result.State is not null && CurrentContext is not null)
                Render(CurrentContext, result.State);
            if (result.Preview is null)
            {
                ReportStatus(result.Error ?? "The destructive Git preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitDestructiveAsync(result.Preview, context.OwnerWindow()))
            {
                ReportStatus("Destructive Git action cancelled; no files were changed.");
                return;
            }

            DeveloperGitIndexCommandResult applied = await service.ApplyDestructiveAsync(
                result.Preview, context.CancellationToken);
            if (applied.State is not null) Render(applied.Context, applied.State);
            if (applied.Error is not null)
            {
                ReportStatus(applied.Error);
                return;
            }
            await context.ReloadOriginalDocumentAsync(selected.Change.Path);
            ReportStatus(action == DeveloperGitDestructiveAction.DiscardTrackedWorktree
                ? $"Discarded working-tree changes in {selected.Change.Path}. Staged content was preserved."
                : $"Deleted untracked path {selected.Change.Path}. Git recovery is not available.");
        });
    }

    private Control BuildActions()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh" };
        Button openDiff = new() { Content = "Open diff" };
        foreach (Button button in new[]
                 { refresh, openDiff, stage, unstage, clearSelection, discard, clean, commit })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(refresh, "Refresh Git working-tree state");
        AutomationProperties.SetName(openDiff, "Open bounded Git working-tree diff");
        AutomationProperties.SetName(stage, "Stage selected Git change");
        AutomationProperties.SetName(unstage, "Unstage selected Git change");
        AutomationProperties.SetName(clearSelection, "Clear Git hunk or line selection");
        AutomationProperties.SetName(discard, "Preview discard of selected tracked Git file");
        AutomationProperties.SetName(clean, "Preview deletion of selected untracked Git file");
        AutomationProperties.SetName(commit, "Compose developer Git commit from staged changes");
        refresh.Click += async (_, _) => await context.RefreshGitAsync();
        openDiff.Click += async (_, _) => await context.OpenGitDiffAsync();
        stage.Click += async (_, _) => await UpdateSelectedIndexAsync(DeveloperGitIndexAction.Stage);
        unstage.Click += async (_, _) => await UpdateSelectedIndexAsync(DeveloperGitIndexAction.Unstage);
        clearSelection.Click += (_, _) => patchUnits.SelectedIndex = -1;
        discard.Click += async (_, _) => await PreviewAndApplyDestructiveAsync(
            DeveloperGitDestructiveAction.DiscardTrackedWorktree);
        clean.Click += async (_, _) => await PreviewAndApplyDestructiveAsync(
            DeveloperGitDestructiveAction.DeleteUntracked);
        commit.Click += async (_, _) => await ComposeAndCommitAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(openDiff);
        actions.Children.Add(stage);
        actions.Children.Add(unstage);
        actions.Children.Add(clearSelection);
        actions.Children.Add(discard);
        actions.Children.Add(clean);
        actions.Children.Add(commit);
        return actions;
    }

    private Control BuildContent()
    {
        Grid panel = new() { RowDefinitions = new("2*,*"), RowSpacing = 8 };
        AutomationProperties.SetName(changes, "Git working-tree changes");
        changes.DoubleTapped += async (_, _) =>
        {
            if (changes.SelectedItem is ChangeChoice choice)
                await context.OpenFileAsync(choice.Change.Path, choice.GoalId);
        };
        changes.SelectionChanged += (_, _) => UpdatePatchUnitChoices();
        panel.Children.Add(changes);
        AutomationProperties.SetName(patchUnits, "Git hunks and changed lines");
        patchUnits.SelectionMode = SelectionMode.Single;
        patchUnits.SelectionChanged += (_, _) => UpdateActionAvailability();
        ToolTip.SetTip(patchUnits,
            "Select one exact hunk or changed line, then choose Stage or Unstage. Clear the selection to act on the whole file.");
        Grid.SetRow(patchUnits, 1);
        panel.Children.Add(patchUnits);
        return panel;
    }

    private void UpdatePatchUnitChoices()
    {
        if (changes.SelectedItem is not ChangeChoice selected)
        {
            patchUnits.ItemsSource = Array.Empty<PatchChoice>();
            UpdateActionAvailability();
            return;
        }
        patchUnits.ItemsSource = currentPatchUnits
            .Where(unit => unit.Path.Value.Equals(selected.Change.Path, StringComparison.Ordinal))
            .Select(unit => new PatchChoice(unit))
            .ToArray();
        patchUnits.SelectedIndex = -1;
        UpdateActionAvailability();
    }

    private void UpdateActionAvailability()
    {
        ChangeChoice? file = changes.SelectedItem as ChangeChoice;
        PatchChoice? patch = patchUnits.SelectedItem as PatchChoice;
        stage.IsEnabled = file is not null && (patch is not null
            ? patch.Unit.Action == DeveloperGitIndexAction.Stage
            : file.Change.IsUnstaged || file.Change.IsConflicted);
        unstage.IsEnabled = file is not null && (patch is not null
            ? patch.Unit.Action == DeveloperGitIndexAction.Unstage
            : file.Change.IsStaged);
        clearSelection.IsEnabled = patch is not null;
        bool original = CurrentContext?.Scope == WorkbenchWorkspaceScope.OriginalWorkspace;
        discard.IsEnabled = original && patch is null && file is not null && file.Change.IsUnstaged &&
                            !file.Change.IsConflicted &&
                            !file.Change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal);
        clean.IsEnabled = original && patch is null && file is not null && file.Change.IsUnstaged &&
                          !file.Change.IsStaged && !file.Change.IsConflicted &&
                          file.Change.WorktreeStatus.Contains("NewInWorkdir", StringComparison.Ordinal);
        commit.IsEnabled = original && CurrentContext is not null &&
                           changes.ItemsSource?.Cast<ChangeChoice>().Any(choice =>
                               choice.Change.IsStaged && !choice.Change.IsConflicted) == true;
    }

    private sealed record ChangeChoice(WorkspaceGitFileChangeView Change, GoalId? GoalId)
    {
        public override string ToString()
        {
            string flags = $"{(Change.IsStaged ? "S" : " ")}{(Change.IsUnstaged ? "M" : " ")}";
            return $"[{flags}]  {Change.Path}" + (Change.IsConflicted ? "  CONFLICT" : string.Empty);
        }
    }

    private sealed record PatchChoice(DeveloperGitPatchUnitView Unit)
    {
        public override string ToString()
        {
            string direction = Unit.Action == DeveloperGitIndexAction.Stage ? "STAGE" : "UNSTAGE";
            return $"[{direction} {Unit.Kind.ToString().ToUpperInvariant()}] {Unit.Label} · {Unit.Preview}";
        }
    }
}
