using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class GitWorktreesTool
{
    private readonly WorkbenchToolContext context;
    private readonly Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState;
    private readonly Action<string> reportStatus;
    private readonly Func<string, ValueTask> manageWorkspaceAtAsync;
    private readonly ListBox worktrees = new();
    private readonly TextBox worktreePath = new() { PlaceholderText = "Absolute worktree path" };
    private readonly TextBox worktreeBranch = new() { PlaceholderText = "Existing or new branch" };
    private readonly CheckBox createWorktreeBranch = new() { Content = "Create new branch at HEAD" };
    private readonly CheckBox forceWorktreeRemove = new() { Content = "Force removal of dirty worktree" };
    private readonly ListBox stashes = new();
    private readonly TextBox stashMessage = new() { PlaceholderText = "Stash message" };
    private readonly CheckBox includeUntrackedInStash = new() { Content = "Include untracked files" };
    private DeveloperGitWorktreeInspectionResult? currentWorktreeInspection;
    private DeveloperGitStashInspectionResult? currentStashInspection;

    internal GitWorktreesTool(
        WorkbenchToolContext context,
        Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState,
        Action<string> reportStatus,
        Func<string, ValueTask> manageWorkspaceAtAsync)
    {
        this.context = context;
        this.renderGitState = renderGitState;
        this.reportStatus = reportStatus;
        this.manageWorkspaceAtAsync = manageWorkspaceAtAsync;
        WorktreesContent = BuildWorktreesContent();
        StashesContent = BuildStashesContent();
    }

    internal Control WorktreesContent { get; }
    internal Control StashesContent { get; }

    internal async ValueTask RefreshWorktreesAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null) return;
        await context.RunAsync(async () => RenderWorktrees(await service.InspectWorktreesAsync(
            context.Request(active), context.CancellationToken)));
    }

    internal async ValueTask CreateWorktreeAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null ||
            currentWorktreeInspection?.State is null ||
            currentWorktreeInspection.WorktreeFingerprint is null)
        {
            reportStatus("Refresh linked worktrees first.");
            return;
        }
        string path = worktreePath.Text?.Trim() ?? string.Empty;
        string branch = worktreeBranch.Text?.Trim() ?? string.Empty;
        await context.RunAsync(async () =>
        {
            DeveloperGitWorktreeInspectionResult result = await service.CreateWorktreeAsync(new(
                context.Request(active), new(currentWorktreeInspection.State.Fingerprint),
                currentWorktreeInspection.WorktreeFingerprint, new(path),
                createWorktreeBranch.IsChecked == true ? null : new(branch),
                createWorktreeBranch.IsChecked == true ? new(branch) : null), context.CancellationToken);
            RenderWorktrees(result);
            if (result.State is not null) renderGitState(result.Context, result.State);
            reportStatus(result.Error ?? $"Created linked worktree at {path}.");
        });
    }

    internal async ValueTask OpenSelectedWorktreeAsync()
    {
        if (context.IsBusy() || worktrees.SelectedItem is not WorktreeChoice selected)
        {
            reportStatus("Select a linked worktree first.");
            return;
        }
        if (selected.Worktree.IsMain)
        {
            reportStatus("The original worktree is already the active workspace.");
            return;
        }
        await manageWorkspaceAtAsync(selected.Worktree.Path.Value);
    }

    internal async ValueTask RemoveSelectedWorktreeAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            currentWorktreeInspection?.State is null ||
            currentWorktreeInspection.WorktreeFingerprint is null ||
            worktrees.SelectedItem is not WorktreeChoice selected)
        {
            reportStatus("Select a current linked worktree first.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitWorktreeRemovePreviewResult result = await service.PreviewWorktreeRemoveAsync(new(
                context.Request(active), new(currentWorktreeInspection.State.Fingerprint),
                currentWorktreeInspection.WorktreeFingerprint, selected.Worktree.Path,
                forceWorktreeRemove.IsChecked == true), context.CancellationToken);
            RenderWorktrees(result.Inspection);
            if (result.Preview is null)
            {
                reportStatus(result.Error ?? "The worktree removal preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitWorktreeRemoveAsync(result.Preview, context.OwnerWindow()))
            {
                reportStatus("Worktree removal cancelled; no directory was deleted.");
                return;
            }
            DeveloperGitWorktreeInspectionResult applied = await service.ApplyWorktreeRemoveAsync(
                result.Preview, context.CancellationToken);
            RenderWorktrees(applied);
            if (applied.State is not null) renderGitState(applied.Context, applied.State);
            reportStatus(applied.Error ?? $"Removed linked worktree {selected.Worktree.Path.Value}.");
        });
    }

    internal void RenderWorktrees(DeveloperGitWorktreeInspectionResult result)
    {
        currentWorktreeInspection = result;
        worktrees.ItemsSource = result.Worktrees.Select(worktree => new WorktreeChoice(worktree)).ToArray();
        worktrees.SelectedIndex = result.Worktrees.Count > 0 ? 0 : -1;
        if (result.Error is not null) reportStatus(result.Error);
    }

    internal async ValueTask RefreshStashesAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null) return;
        await context.RunAsync(async () => RenderStashes(await service.InspectStashesAsync(
            context.Request(active), context.CancellationToken)));
    }

    internal async ValueTask CreateStashAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null || currentStashInspection?.State is null)
        {
            reportStatus("Refresh Git stashes first.");
            return;
        }
        string message = stashMessage.Text?.Trim() ?? string.Empty;
        await context.RunAsync(async () =>
        {
            DeveloperGitStashInspectionResult result = await service.CreateStashAsync(new(
                context.Request(active), new(currentStashInspection.State.Fingerprint), new(message),
                includeUntrackedInStash.IsChecked == true), context.CancellationToken);
            RenderStashes(result);
            if (result.State is not null) renderGitState(result.Context, result.State);
            reportStatus(result.Error ?? "Created a new stash from the displayed working state.");
        });
    }

    internal async ValueTask ApplySelectedStashAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null ||
            currentStashInspection?.State is null || stashes.SelectedItem is not StashChoice selected)
        {
            reportStatus("Select a current Git stash first.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitStashInspectionResult result = await service.ApplyStashAsync(new(
                context.Request(active), new(currentStashInspection.State.Fingerprint),
                selected.Stash.CommitSha), context.CancellationToken);
            RenderStashes(result);
            if (result.State is not null) renderGitState(result.Context, result.State);
            reportStatus(result.Error ??
                $"Applied {selected.Stash.Selector}; the stash remains available until explicitly deleted.");
        });
    }

    internal async ValueTask DropSelectedStashAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            currentStashInspection?.State is null || stashes.SelectedItem is not StashChoice selected)
        {
            reportStatus("Select a current Git stash first.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitStashDropPreviewResult result = await service.PreviewStashDropAsync(new(
                context.Request(active), new(currentStashInspection.State.Fingerprint),
                selected.Stash.CommitSha), context.CancellationToken);
            RenderStashes(result.Inspection);
            if (result.Preview is null)
            {
                reportStatus(result.Error ?? "The stash deletion preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitStashDropAsync(result.Preview, context.OwnerWindow()))
            {
                reportStatus("Stash deletion cancelled; the stash remains available.");
                return;
            }
            DeveloperGitStashInspectionResult applied = await service.ApplyStashDropAsync(
                result.Preview, context.CancellationToken);
            RenderStashes(applied);
            if (applied.State is not null) renderGitState(applied.Context, applied.State);
            reportStatus(applied.Error ?? $"Deleted stash {selected.Stash.Selector}.");
        });
    }

    internal void RenderStashes(DeveloperGitStashInspectionResult result)
    {
        currentStashInspection = result;
        stashes.ItemsSource = result.Stashes.Select(stash => new StashChoice(stash)).ToArray();
        stashes.SelectedIndex = result.Stashes.Count > 0 ? 0 : -1;
        if (result.Error is not null) reportStatus(result.Error);
    }

    private Control BuildWorktreesContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh worktrees" };
        Button create = new() { Content = "Create" };
        Button open = new() { Content = "Open as workspace…" };
        Button remove = new() { Content = "Remove…" };
        foreach (Button button in new[] { refresh, create, open, remove })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(refresh, "Refresh linked Git worktrees");
        AutomationProperties.SetName(create, "Create linked Git worktree");
        AutomationProperties.SetName(open, "Open selected linked Git worktree as a workspace");
        AutomationProperties.SetName(remove, "Preview removal of selected linked Git worktree");
        AutomationProperties.SetName(worktreePath, "New linked Git worktree absolute path");
        AutomationProperties.SetName(worktreeBranch, "Linked Git worktree branch name");
        AutomationProperties.SetName(createWorktreeBranch, "Create new local branch for linked Git worktree");
        AutomationProperties.SetName(forceWorktreeRemove, "Force removal of dirty linked Git worktree");
        AutomationProperties.SetName(worktrees, "Linked Git worktrees");
        refresh.Click += async (_, _) => await RefreshWorktreesAsync();
        create.Click += async (_, _) => await CreateWorktreeAsync();
        open.Click += async (_, _) => await OpenSelectedWorktreeAsync();
        remove.Click += async (_, _) => await RemoveSelectedWorktreeAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(create);
        actions.Children.Add(open);
        actions.Children.Add(remove);
        actions.Children.Add(createWorktreeBranch);
        actions.Children.Add(forceWorktreeRemove);
        Grid panel = new() { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 8 };
        panel.Children.Add(worktreePath);
        Grid.SetRow(worktreeBranch, 1);
        panel.Children.Add(worktreeBranch);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);
        Grid.SetRow(worktrees, 3);
        panel.Children.Add(worktrees);
        return panel;
    }

    private Control BuildStashesContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh stashes" };
        Button create = new() { Content = "Create stash" };
        Button apply = new() { Content = "Apply" };
        Button drop = new() { Content = "Delete…" };
        foreach (Button button in new[] { refresh, create, apply, drop })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(refresh, "Refresh local Git stashes");
        AutomationProperties.SetName(create, "Create Git stash from displayed working state");
        AutomationProperties.SetName(apply, "Apply selected Git stash and keep it");
        AutomationProperties.SetName(drop, "Preview deletion of selected Git stash");
        AutomationProperties.SetName(stashMessage, "New Git stash message");
        AutomationProperties.SetName(includeUntrackedInStash, "Include untracked files in new Git stash");
        AutomationProperties.SetName(stashes, "Local Git stashes");
        refresh.Click += async (_, _) => await RefreshStashesAsync();
        create.Click += async (_, _) => await CreateStashAsync();
        apply.Click += async (_, _) => await ApplySelectedStashAsync();
        drop.Click += async (_, _) => await DropSelectedStashAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(create);
        actions.Children.Add(apply);
        actions.Children.Add(drop);
        actions.Children.Add(includeUntrackedInStash);
        Grid panel = new() { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 8 };
        panel.Children.Add(stashMessage);
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);
        Grid.SetRow(stashes, 2);
        panel.Children.Add(stashes);
        return panel;
    }

    private sealed record WorktreeChoice(DeveloperGitWorktreeView Worktree)
    {
        public override string ToString()
        {
            string branch = Worktree.Branch?.Value ?? "detached HEAD";
            string flags = (Worktree.IsMain ? " · original" : string.Empty) +
                           (Worktree.IsDirty ? " · dirty" : string.Empty) +
                           (Worktree.HasConflicts ? " · conflicts" : string.Empty) +
                           (Worktree.IsLocked ? " · locked" : string.Empty) +
                           (Worktree.IsHarnessManaged ? " · goal-managed" : string.Empty) +
                           (Worktree.IsRegisteredWorkspace ? " · registered" : string.Empty);
            return $"{branch} · {Worktree.HeadSha[..Math.Min(8, Worktree.HeadSha.Length)]} · " +
                   $"{Worktree.Path.Value}{flags}";
        }
    }

    private sealed record StashChoice(DeveloperGitStashView Stash)
    {
        public override string ToString() =>
            $"{Stash.Selector} · {Stash.CommitSha.Value[..Math.Min(8, Stash.CommitSha.Value.Length)]} · " +
            $"{Stash.CreatedAt.LocalDateTime:g} · {Stash.Message}" +
            (Stash.MessageIsTruncated ? "…" : string.Empty);
    }
}
