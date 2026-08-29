using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class GitRemotesTool
{
    private readonly WorkbenchToolContext context;
    private readonly Action<WorkbenchWorkspaceContext, WorkspaceGitStateView> renderGitState;
    private readonly Action<string> reportStatus;
    private readonly Func<ValueTask<bool>> prepareForWorkspaceChangeAsync;
    private readonly Func<ValueTask> refreshWorkspaceContextAsync;
    private readonly ListBox remotes = new();
    private readonly TextBox source = new() { PlaceholderText = "Source branch" };
    private readonly TextBox destination = new() { PlaceholderText = "Destination branch" };
    private readonly CheckBox rebasePull = new() { Content = "Rebase integration" };
    private readonly CheckBox forceWithLeasePush = new() { Content = "Force with lease" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private DeveloperGitRemoteInspectionResult? currentInspection;

    internal GitRemotesTool(
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
        Content = BuildContent();
    }

    internal Control Content { get; }

    internal async ValueTask RefreshAsync()
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        if (context.IsBusy() || active is null || service is null) return;
        await context.RunAsync(async () => Render(await service.InspectRemotesAsync(
            context.Request(active), context.CancellationToken)));
    }

    internal void Render(DeveloperGitRemoteInspectionResult result)
    {
        currentInspection = result;
        remotes.ItemsSource = result.Remotes.Select(remote => new RemoteChoice(remote)).ToArray();
        int selected = result.UpstreamRemote is null ? 0 : result.Remotes.ToList().FindIndex(remote =>
            remote.Name == result.UpstreamRemote);
        remotes.SelectedIndex = result.Remotes.Count == 0 ? -1 : Math.Max(0, selected);
        if (string.IsNullOrWhiteSpace(source.Text))
            source.Text = result.LocalBranch?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(destination.Text))
            destination.Text = result.UpstreamBranch?.Value ?? result.LocalBranch?.Value ?? string.Empty;
        status.Text = result.Error ??
            $"Local {result.LocalSha ?? "unborn"} · remote tracking {result.RemoteTrackingSha ?? "unknown"} · " +
            $"ahead {result.Ahead?.ToString() ?? "?"} · behind {result.Behind?.ToString() ?? "?"}";
    }

    internal async ValueTask SynchronizeAsync(DeveloperGitRemoteAction action)
    {
        WorkspaceView? active = context.ActiveWorkspace();
        IDeveloperGitService? service = context.DeveloperGitService;
        IWorkbenchDocumentPrompt? prompt = context.DocumentPrompt;
        if (context.IsBusy() || active is null || service is null || prompt is null ||
            currentInspection?.State is null || remotes.SelectedItem is not RemoteChoice selected)
        {
            reportStatus("Refresh and select a configured Git remote first.");
            return;
        }
        string sourceName = source.Text?.Trim() ?? string.Empty;
        string destinationName = destination.Text?.Trim() ?? string.Empty;
        DeveloperGitPushPolicy policy = forceWithLeasePush.IsChecked == true
            ? DeveloperGitPushPolicy.ForceWithLease : DeveloperGitPushPolicy.FastForwardOnly;
        if ((action is DeveloperGitRemoteAction.PullMerge or DeveloperGitRemoteAction.PullRebase) &&
            !await prepareForWorkspaceChangeAsync())
        {
            reportStatus("Remote integration cancelled; unsaved documents remain open.");
            return;
        }
        await context.RunAsync(async () =>
        {
            DeveloperGitRemotePreviewResult result = await service.PreviewRemoteAsync(new(
                context.Request(active), new(currentInspection.State.Fingerprint), action,
                selected.Remote.Name, new(sourceName), new(destinationName), policy),
                context.CancellationToken);
            Render(result.Inspection);
            if (result.Preview is null)
            {
                reportStatus(result.Error ?? "The Git remote operation preview is unavailable.");
                return;
            }
            if (!await prompt.ConfirmGitRemoteAsync(result.Preview, context.OwnerWindow()))
            {
                reportStatus("Git remote operation cancelled; no network or integration action ran.");
                return;
            }
            DeveloperGitRemoteInspectionResult applied = await service.ApplyRemoteAsync(
                result.Preview, context.CancellationToken);
            Render(applied);
            if (applied.State is not null) renderGitState(applied.Context, applied.State);
            reportStatus(applied.Error ?? $"Git {action} completed for {selected.Remote.Name.Value}.");
            if (applied.Error is null &&
                action is DeveloperGitRemoteAction.PullMerge or DeveloperGitRemoteAction.PullRebase)
                await refreshWorkspaceContextAsync();
        });
    }

    private Control BuildContent()
    {
        WrapPanel actions = new() { Orientation = Orientation.Horizontal };
        Button refresh = new() { Content = "Refresh remotes" };
        Button fetch = new() { Content = "Fetch…" };
        Button pull = new() { Content = "Integrate fetched…" };
        Button push = new() { Content = "Push…" };
        foreach (Button button in new[] { refresh, fetch, pull, push })
            button.Margin = new Thickness(0, 0, 6, 6);
        AutomationProperties.SetName(remotes, "Configured Git remotes with sanitized URLs");
        AutomationProperties.SetName(source, "Git remote source branch");
        AutomationProperties.SetName(destination, "Git remote destination branch");
        AutomationProperties.SetName(status, "Git remote divergence and observed commits");
        AutomationProperties.SetName(refresh, "Refresh Git remotes and divergence");
        AutomationProperties.SetName(fetch, "Preview explicit Git fetch");
        AutomationProperties.SetName(pull, "Preview integration of already fetched commits");
        AutomationProperties.SetName(push, "Preview explicit Git push");
        AutomationProperties.SetName(rebasePull, "Use rebase when integrating fetched commits");
        AutomationProperties.SetName(forceWithLeasePush, "Use force with exact lease for Git push");
        refresh.Click += async (_, _) => await RefreshAsync();
        fetch.Click += async (_, _) => await SynchronizeAsync(DeveloperGitRemoteAction.Fetch);
        pull.Click += async (_, _) => await SynchronizeAsync(rebasePull.IsChecked == true
            ? DeveloperGitRemoteAction.PullRebase : DeveloperGitRemoteAction.PullMerge);
        push.Click += async (_, _) => await SynchronizeAsync(DeveloperGitRemoteAction.Push);
        actions.Children.Add(refresh);
        actions.Children.Add(fetch);
        actions.Children.Add(pull);
        actions.Children.Add(push);
        actions.Children.Add(rebasePull);
        actions.Children.Add(forceWithLeasePush);
        Grid refs = new() { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        refs.Children.Add(source);
        Grid.SetColumn(destination, 1);
        refs.Children.Add(destination);
        Grid panel = new() { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 8 };
        panel.Children.Add(status);
        Grid.SetRow(refs, 1);
        panel.Children.Add(refs);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);
        Grid.SetRow(remotes, 3);
        panel.Children.Add(remotes);
        TextBlock guidance = new()
        {
            Text = "Pull is deliberately split: Fetch first, review divergence, then integrate the fetched tracking ref.",
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(guidance, 4);
        panel.Children.Add(guidance);
        return panel;
    }

    private sealed record RemoteChoice(DeveloperGitRemoteView Remote)
    {
        public override string ToString() => $"{Remote.Name.Value} · {Remote.SanitizedUrl}";
    }
}
