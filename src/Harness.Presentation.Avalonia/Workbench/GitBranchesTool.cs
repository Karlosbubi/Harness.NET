using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class GitBranchesTool
{
    private readonly WorkbenchToolContext context;
    private readonly Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState;
    private readonly Action<string> reportStatus;
    private readonly Func<ValueTask<bool>> prepareForWorkspaceChangeAsync;
    private readonly Func<ValueTask> refreshWorkspaceContextAsync;
    private readonly ListBox branches = new();
    private readonly TextBox branchName = new() { PlaceholderText = "New branch name" };
    private readonly CheckBox forceBranchDelete = new() { Content = "Force unmerged deletion" };
    private readonly ListBox tags = new();
    private readonly TextBox tagName = new() { PlaceholderText = "Tag name" };
    private readonly TextBox tagMessage = new() { PlaceholderText = "Annotated tag message" };
    private readonly CheckBox annotatedTag = new() { Content = "Annotated tag" };
    private DeveloperGitBranchInspectionResult? currentBranchInspection;
    private DeveloperGitTagInspectionResult? currentTagInspection;

    internal GitBranchesTool(
        WorkbenchToolContext context,
        Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState,
        Action<string> reportStatus,
        Func<ValueTask<bool>> prepareForWorkspaceChangeAsync,
        Func<ValueTask> refreshWorkspaceContextAsync)
    {
        this.context = context;
        this.renderGitState = renderGitState;
        this.reportStatus = reportStatus;
        this.prepareForWorkspaceChangeAsync = prepareForWorkspaceChangeAsync;
        this.refreshWorkspaceContextAsync = refreshWorkspaceContextAsync;
        BranchesContent = BuildBranchesContent();
        TagsContent = BuildTagsContent();
    }

    internal Control BranchesContent { get; }
    internal Control TagsContent { get; }

    internal async ValueTask RefreshBranchesAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null) return;
        await context.RunAsync(async () => RenderBranches(await service.InspectBranchesAsync(
            context.Request(active), context.CancellationToken)));
    }

    internal async ValueTask ApplyBranchAsync(DeveloperGitBranchAction action)
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        DeveloperGitBranchView? selected = (branches.SelectedItem as BranchChoice)?.Branch;
        if (context.IsBusy() || active is null || service is null ||
            currentBranchInspection?.State is null)
        {
            reportStatus("Refresh local branches first.");
            return;
        }
        bool changesActiveContext = action == DeveloperGitBranchAction.Switch ||
                                    action == DeveloperGitBranchAction.Rename && selected?.IsCurrent == true;
        if (changesActiveContext && selected is not null &&
            !await prepareForWorkspaceChangeAsync())
        {
            reportStatus("Branch switch cancelled; unsaved documents remain open.");
            return;
        }
        string name = branchName.Text?.Trim() ?? string.Empty;
        await context.RunAsync(async () =>
        {
            DeveloperGitBranchInspectionResult result = await service.ApplyBranchAsync(new(
                context.Request(active), new(currentBranchInspection.State.Fingerprint), action,
                selected?.Name, string.IsNullOrWhiteSpace(name) ? null : new(name)),
                context.CancellationToken);
            RenderBranches(result);
            if (result.State is not null) renderGitState(result.Context, result.State);
            if (result.Error is not null)
            {
                reportStatus(result.Error);
                return;
            }
            if (action == DeveloperGitBranchAction.Switch ||
                action == DeveloperGitBranchAction.Rename && selected?.IsCurrent == true)
                await refreshWorkspaceContextAsync();
            reportStatus($"Branch {action.ToString().ToLowerInvariant()} completed.");
        });
    }

    internal async ValueTask DeleteSelectedBranchAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            currentBranchInspection?.State is null ||
            branches.SelectedItem is not BranchChoice selected)
        {
            reportStatus("Select a current local branch first.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitBranchDeletePreviewResult result = await service.PreviewBranchDeleteAsync(new(
                context.Request(active), new(currentBranchInspection.State.Fingerprint),
                selected.Branch.Name, forceBranchDelete.IsChecked == true), context.CancellationToken);
            RenderBranches(result.Inspection);
            if (result.Preview is null)
            {
                reportStatus(result.Error ?? "The branch deletion preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitBranchDeleteAsync(result.Preview, context.OwnerWindow()))
            {
                reportStatus("Branch deletion cancelled; no reference was changed.");
                return;
            }
            DeveloperGitBranchInspectionResult applied = await service.ApplyBranchDeleteAsync(
                result.Preview, context.CancellationToken);
            RenderBranches(applied);
            if (applied.State is not null) renderGitState(applied.Context, applied.State);
            reportStatus(applied.Error ?? $"Deleted local branch {selected.Branch.Name.Value}.");
        });
    }

    internal void RenderBranches(DeveloperGitBranchInspectionResult result)
    {
        currentBranchInspection = result;
        branches.ItemsSource = result.Branches.Select(branch => new BranchChoice(branch)).ToArray();
        branches.SelectedIndex = result.Branches.Count > 0 ? 0 : -1;
        if (result.Error is not null) reportStatus(result.Error);
    }

    internal async ValueTask RefreshTagsAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null) return;
        await context.RunAsync(async () => RenderTags(await service.InspectTagsAsync(
            context.Request(active), context.CancellationToken)));
    }

    internal async ValueTask CreateTagAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null || currentTagInspection?.State is null)
        {
            reportStatus("Refresh local tags first.");
            return;
        }
        string name = tagName.Text?.Trim() ?? string.Empty;
        string message = tagMessage.Text?.Trim() ?? string.Empty;
        await context.RunAsync(async () =>
        {
            DeveloperGitTagInspectionResult result = await service.CreateTagAsync(new(
                context.Request(active), new(currentTagInspection.State.Fingerprint), new(name),
                annotatedTag.IsChecked == true,
                string.IsNullOrWhiteSpace(message) ? null : new(message)), context.CancellationToken);
            RenderTags(result);
            if (result.State is not null) renderGitState(result.Context, result.State);
            reportStatus(result.Error ?? $"Created local tag {name}.");
        });
    }

    internal async ValueTask DeleteSelectedTagAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            currentTagInspection?.State is null || tags.SelectedItem is not TagChoice selected)
        {
            reportStatus("Select a current local tag first.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitTagDeletePreviewResult result = await service.PreviewTagDeleteAsync(new(
                context.Request(active), new(currentTagInspection.State.Fingerprint),
                selected.Tag.Name), context.CancellationToken);
            RenderTags(result.Inspection);
            if (result.Preview is null)
            {
                reportStatus(result.Error ?? "The tag deletion preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitTagDeleteAsync(result.Preview, context.OwnerWindow()))
            {
                reportStatus("Tag deletion cancelled; no reference was changed.");
                return;
            }
            DeveloperGitTagInspectionResult applied = await service.ApplyTagDeleteAsync(
                result.Preview, context.CancellationToken);
            RenderTags(applied);
            if (applied.State is not null) renderGitState(applied.Context, applied.State);
            reportStatus(applied.Error ?? $"Deleted local tag {selected.Tag.Name.Value}.");
        });
    }

    internal void RenderTags(DeveloperGitTagInspectionResult result)
    {
        currentTagInspection = result;
        tags.ItemsSource = result.Tags.Select(tag => new TagChoice(tag)).ToArray();
        tags.SelectedIndex = result.Tags.Count > 0 ? 0 : -1;
        if (result.Error is not null) reportStatus(result.Error);
    }

    private Control BuildBranchesContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh branches" };
        Button create = new() { Content = "Create" };
        Button switchBranch = new() { Content = "Switch" };
        Button rename = new() { Content = "Rename" };
        Button delete = new() { Content = "Delete" };
        foreach (Button button in new[] { refresh, create, switchBranch, rename, delete })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(refresh, "Refresh local Git branches");
        AutomationProperties.SetName(create, "Create local Git branch");
        AutomationProperties.SetName(switchBranch, "Switch to selected local Git branch");
        AutomationProperties.SetName(rename, "Rename selected local Git branch");
        AutomationProperties.SetName(delete, "Preview deletion of selected local Git branch");
        refresh.Click += async (_, _) => await RefreshBranchesAsync();
        create.Click += async (_, _) => await ApplyBranchAsync(DeveloperGitBranchAction.Create);
        switchBranch.Click += async (_, _) => await ApplyBranchAsync(DeveloperGitBranchAction.Switch);
        rename.Click += async (_, _) => await ApplyBranchAsync(DeveloperGitBranchAction.Rename);
        delete.Click += async (_, _) => await DeleteSelectedBranchAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(create);
        actions.Children.Add(switchBranch);
        actions.Children.Add(rename);
        actions.Children.Add(delete);
        actions.Children.Add(forceBranchDelete);
        AutomationProperties.SetName(branchName, "New local Git branch name");
        AutomationProperties.SetName(forceBranchDelete, "Force deletion of unmerged local Git branch");
        AutomationProperties.SetName(branches, "Local Git branches");
        Grid panel = new() { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 8 };
        panel.Children.Add(branchName);
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);
        Grid.SetRow(branches, 2);
        panel.Children.Add(branches);
        return panel;
    }

    private Control BuildTagsContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh tags" };
        Button create = new() { Content = "Create tag" };
        Button delete = new() { Content = "Delete tag" };
        foreach (Button button in new[] { refresh, create, delete })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(refresh, "Refresh local Git tags");
        AutomationProperties.SetName(create, "Create local Git tag at HEAD");
        AutomationProperties.SetName(delete, "Preview deletion of selected local Git tag");
        AutomationProperties.SetName(tagName, "New local Git tag name");
        AutomationProperties.SetName(tagMessage, "Annotated local Git tag message");
        AutomationProperties.SetName(annotatedTag, "Create annotated local Git tag");
        AutomationProperties.SetName(tags, "Local Git tags");
        refresh.Click += async (_, _) => await RefreshTagsAsync();
        create.Click += async (_, _) => await CreateTagAsync();
        delete.Click += async (_, _) => await DeleteSelectedTagAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(create);
        actions.Children.Add(delete);
        actions.Children.Add(annotatedTag);
        Grid panel = new() { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 8 };
        panel.Children.Add(tagName);
        Grid.SetRow(tagMessage, 1);
        panel.Children.Add(tagMessage);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);
        Grid.SetRow(tags, 3);
        panel.Children.Add(tags);
        return panel;
    }

    private sealed record BranchChoice(DeveloperGitBranchView Branch)
    {
        public override string ToString() =>
            $"{(Branch.IsCurrent ? "● " : string.Empty)}{Branch.Name.Value} · " +
            $"{Branch.TipSha[..Math.Min(8, Branch.TipSha.Length)]}" +
            (Branch.IsMergedIntoHead ? " · merged" : string.Empty);
    }

    private sealed record TagChoice(DeveloperGitTagView Tag)
    {
        public override string ToString() =>
            $"{Tag.Name.Value} · {Tag.TargetSha[..Math.Min(8, Tag.TargetSha.Length)]}" +
            (Tag.IsAnnotated ? " · annotated" : string.Empty);
    }
}
