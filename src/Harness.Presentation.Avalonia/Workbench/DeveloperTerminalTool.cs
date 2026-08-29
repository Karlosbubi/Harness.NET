using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.Terminal;
using Harness.BusinessLogic.Workspaces;
using SvcSystems.UI.Terminal;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed partial class DeveloperTerminalTool
{
    private const int InitialColumns = 100;
    private const int InitialRows = 30;
    private const int MaximumDetectedLinks = 32;
    private readonly IDeveloperTerminalService? service;
    private readonly Func<AvaloniaShellState> state;
    private readonly CancellationToken applicationCancellation;
    private readonly ISensitiveDisplayGuard? sensitiveDisplayGuard;
    private readonly List<PresentedSession> sessions = [];
    private readonly ComboBox sessionPicker = new() { MinWidth = 180 };
    private readonly Button create = new() { Content = "New terminal" };
    private readonly Button stop = new() { Content = "Stop", IsEnabled = false };
    private readonly Button close = new() { Content = "Close", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy", IsEnabled = false };
    private readonly Button paste = new() { Content = "Paste", IsEnabled = false };
    private readonly TextBox search = new() { PlaceholderText = "Search scrollback", MinWidth = 160 };
    private readonly Button previous = new() { Content = "↑", IsEnabled = false };
    private readonly Button next = new() { Content = "↓", IsEnabled = false };
    private readonly TextBlock searchStatus = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox links = new() { MinWidth = 180, IsEnabled = false };
    private readonly Button openLink = new() { Content = "Open link", IsEnabled = false };
    private readonly TextBlock metadata = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl terminalHost = new();
    private readonly SemaphoreSlim restoreGate = new(1, 1);
    private ISensitiveDisplayLease? sensitiveDisplayLease;
    private string? restorationContext;

    internal DeveloperTerminalTool(
        IDeveloperTerminalService? service,
        Func<AvaloniaShellState> state,
        CancellationToken applicationCancellation,
        ISensitiveDisplayGuard? sensitiveDisplayGuard = null)
    {
        this.service = service;
        this.state = state;
        this.applicationCancellation = applicationCancellation;
        this.sensitiveDisplayGuard = sensitiveDisplayGuard;
        Content = BuildContent();
        UpdateAvailability();
    }

    internal Control Content { get; }
    internal ComboBox SessionPicker => sessionPicker;
    internal Button CreateButton => create;
    internal Button StopButton => stop;
    internal Button CloseButton => close;
    internal TextBlock Metadata => metadata;
    internal TextBlock Status => status;
    internal TerminalControl? ActiveTerminal => Selected?.Control;

    internal void UpdateAvailability()
    {
        WorkspaceView? workspace = ActiveWorkspace();
        create.IsEnabled = service is not null && workspace is { IsTrusted: true };
        if (sessions.Count == 0)
        {
            status.Text = service is null
                ? "Interactive terminals are unavailable in this presentation."
                : workspace is null
                    ? "Open a workspace to start a terminal."
                    : !workspace.IsTrusted
                        ? "Trust the workspace before starting a terminal."
                        : "Ready to start a trusted developer-only terminal.";
        }
    }

    internal void Update(AvaloniaShellState snapshot)
    {
        UpdateAvailability();
        WorkspaceView? workspace = snapshot.Workspaces.Registered
            .FirstOrDefault(item => item.IsActive);
        GoalView? goal = snapshot.Goals.SelectedGoal;
        string? goalId = goal is not null && goal.WorkspaceId == workspace?.Id
            ? goal.Id.Value
            : null;
        string? nextContext = workspace is { IsTrusted: true }
            ? $"{workspace.Id}\n{goalId ?? string.Empty}"
            : null;
        if (service is null || string.Equals(restorationContext, nextContext,
                StringComparison.Ordinal))
        {
            return;
        }

        restorationContext = nextContext;
        if (nextContext is null)
        {
            return;
        }

        WorkbenchWorkspaceRequest request = new(new(workspace!.Id),
            goalId is null ? null : new GoalId(goalId));
        Dispatcher.UIThread.Post(async () =>
            await RestoreMetadataAsync(request, nextContext));
    }

    internal async ValueTask CreateAsync()
    {
        WorkspaceView? workspace = ActiveWorkspace();
        if (service is null || workspace is not { IsTrusted: true })
        {
            UpdateAvailability();
            return;
        }

        create.IsEnabled = false;
        status.Text = "Starting a trusted pseudo-terminal…";
        bool acquiredDisplay = false;
        try
        {
            if (sensitiveDisplayLease is null && sensitiveDisplayGuard is not null)
            {
                if (!sensitiveDisplayGuard.TryBeginSensitiveDisplay(
                        SensitiveDisplayKind.DeveloperTerminal,
                        out sensitiveDisplayLease))
                {
                    status.Text = "Wait for the active visual capture or hide the other sensitive value first.";
                    return;
                }

                acquiredDisplay = true;
            }

            GoalView? goal = state().Goals.SelectedGoal;
            WorkbenchWorkspaceRequest request = new(
                new(workspace.Id),
                goal?.WorkspaceId == workspace.Id ? goal.Id : null);
            DeveloperTerminalStartResult result = await service.StartAsync(
                new(request, new(InitialColumns, InitialRows)),
                applicationCancellation);
            if (result.Session is null)
            {
                if (acquiredDisplay) ReleaseSensitiveDisplay();
                status.Text = result.Error ?? "The terminal could not be started.";
                return;
            }

            PresentedSession presented = CreatePresentedSession(result.Session,
                hasSensitiveContent: true);
            sessions.Add(presented);
            RefreshSessionChoices(presented);
            presented.OutputLoop = ReadOutputAsync(presented);
            status.Text = "Terminal running. Content is transient and developer-only.";
        }
        catch (OperationCanceledException)
        {
            if (acquiredDisplay) ReleaseSensitiveDisplay();
            status.Text = "Terminal creation cancelled.";
        }
        finally
        {
            if (acquiredDisplay && sessions.All(item => !item.HasSensitiveContent))
            {
                ReleaseSensitiveDisplay();
            }
            UpdateAvailability();
        }
    }

    private async ValueTask RestoreMetadataAsync(
        WorkbenchWorkspaceRequest request,
        string expectedContext)
    {
        if (service is null)
        {
            return;
        }

        bool acquired = false;
        try
        {
            await restoreGate.WaitAsync(applicationCancellation);
            acquired = true;
            if (!string.Equals(restorationContext, expectedContext, StringComparison.Ordinal))
            {
                return;
            }

            DeveloperTerminalListResult result = await service.ListAsync(
                request, applicationCancellation);
            if (!string.Equals(restorationContext, expectedContext, StringComparison.Ordinal))
            {
                return;
            }

            PresentedSession? newest = null;
            foreach (DeveloperTerminalSessionView view in result.Sessions
                         .OrderBy(item => item.StartedAt))
            {
                PresentedSession? existing = sessions.FirstOrDefault(item =>
                    item.View.Id == view.Id);
                if (existing is not null)
                {
                    if (!existing.HasSensitiveContent) existing.View = view;
                    newest = existing;
                    continue;
                }

                PresentedSession restored = CreatePresentedSession(view,
                    hasSensitiveContent: false);
                byte[] notice = Encoding.UTF8.GetBytes(
                    "Terminal content was not persisted. No process was restored.\r\n");
                restored.Model.Feed(notice, notice.Length);
                sessions.Add(restored);
                newest = restored;
            }

            if (newest is not null)
            {
                RefreshSessionChoices(newest);
                status.Text = "Restored terminal lifecycle metadata; content and processes were not restored.";
            }
        }
        catch (OperationCanceledException) when (applicationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            status.Text = "Saved terminal lifecycle metadata is temporarily unavailable.";
        }
        finally
        {
            if (acquired) restoreGate.Release();
        }
    }

    internal async ValueTask StopAsync()
    {
        PresentedSession? selected = Selected;
        if (service is null || selected is null ||
            selected.View.State != DeveloperTerminalSessionState.Running)
        {
            return;
        }

        stop.IsEnabled = false;
        status.Text = "Stopping the complete terminal process tree…";
        DeveloperTerminalSessionResult result = await service.StopAsync(
            selected.View.Id,
            applicationCancellation);
        if (result.Session is not null)
        {
            selected.View = result.Session;
        }

        status.Text = result.Error ?? "Terminal stopped.";
        RenderSelected();
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 6,
            Margin = new(10),
        };
        WrapPanel sessionsBar = new();
        AutomationProperties.SetName(sessionPicker, "Developer terminal sessions");
        sessionPicker.SelectionChanged += (_, _) => RenderSelected();
        sessionsBar.Children.Add(sessionPicker);
        AutomationProperties.SetName(create, "Create trusted developer terminal");
        create.Click += async (_, _) => await CreateAsync();
        sessionsBar.Children.Add(create);
        AutomationProperties.SetName(stop, "Stop terminal process tree");
        stop.Click += async (_, _) => await StopAsync();
        sessionsBar.Children.Add(stop);
        AutomationProperties.SetName(close, "Close completed terminal view");
        close.Click += (_, _) => CloseSelected();
        sessionsBar.Children.Add(close);
        sessionsBar.Children.Add(copy);
        copy.Click += async (_, _) =>
        {
            if (Selected is { } selected) await selected.Control.CopySelectionAsync();
        };
        sessionsBar.Children.Add(paste);
        paste.Click += async (_, _) =>
        {
            if (Selected is { } selected) await selected.Control.PasteFromClipboardAsync();
        };
        root.Children.Add(sessionsBar);

        AutomationProperties.SetName(metadata, "Terminal source and persistence metadata");
        Grid.SetRow(metadata, 1);
        root.Children.Add(metadata);

        WrapPanel findBar = new();
        AutomationProperties.SetName(search, "Search terminal scrollback");
        search.TextChanged += (_, _) => SearchSelected();
        findBar.Children.Add(search);
        previous.Click += (_, _) => SelectPreviousSearchResult();
        AutomationProperties.SetName(previous, "Previous terminal search result");
        findBar.Children.Add(previous);
        next.Click += (_, _) => SelectNextSearchResult();
        AutomationProperties.SetName(next, "Next terminal search result");
        findBar.Children.Add(next);
        findBar.Children.Add(searchStatus);
        AutomationProperties.SetName(links, "Detected terminal links");
        links.SelectionChanged += (_, _) => openLink.IsEnabled = links.SelectedItem is UriChoice;
        findBar.Children.Add(links);
        AutomationProperties.SetName(openLink, "Open selected terminal link");
        openLink.Click += async (_, _) => await OpenSelectedLinkAsync();
        findBar.Children.Add(openLink);
        Grid.SetRow(findBar, 2);
        root.Children.Add(findBar);

        terminalHost.Content = new TextBlock
        {
            Text = "Start a terminal to open an interactive shell.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(terminalHost, "Interactive developer terminal");
        Grid.SetRow(terminalHost, 3);
        root.Children.Add(terminalHost);

        AutomationProperties.SetName(status, "Developer terminal status");
        Grid.SetRow(status, 4);
        root.Children.Add(status);
        return root;
    }

    private PresentedSession CreatePresentedSession(
        DeveloperTerminalSessionView view,
        bool hasSensitiveContent)
    {
        TerminalControlModel model = new(new TerminalOptions
        {
            Cols = view.Dimensions.Columns,
            Rows = view.Dimensions.Rows,
            Scrollback = 5_000,
            ReflowOnResize = false,
            TermName = "xterm-256color",
        });
        TerminalControl control = new()
        {
            Model = model,
            FontFamily = new("Cascadia Mono, JetBrains Mono, monospace"),
            FontSize = 12,
            RightClickAction = RightClickAction.CopyOrPaste,
        };
        AutomationProperties.SetName(control, $"Terminal {view.Shell.Value}");
        PresentedSession presented = new(
            view,
            model,
            control,
            CancellationTokenSource.CreateLinkedTokenSource(applicationCancellation),
            hasSensitiveContent);
        model.UserInput += (_, args) => _ = WriteInputAsync(presented, args.Data.ToArray());
        model.SizeChanged += (_, args) => QueueResize(presented, args.Cols, args.Rows);
        return presented;
    }

    private async Task ReadOutputAsync(PresentedSession presented)
    {
        if (service is null)
        {
            return;
        }

        try
        {
            while (!presented.Cancellation.IsCancellationRequested)
            {
                DeveloperTerminalReadResult read = await service.ReadAsync(
                    presented.View.Id,
                    presented.Cancellation.Token);
                if (!read.Data.Value.IsEmpty)
                {
                    byte[] bytes = read.Data.Value.ToArray();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        presented.Model.Feed(bytes, bytes.Length);
                        DetectLinks(presented, bytes);
                    });
                }

                if (read.EndOfStream)
                {
                    break;
                }
            }

            DeveloperTerminalSessionResult current = await service.GetAsync(
                presented.View.Id,
                CancellationToken.None);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (current.Session is not null) presented.View = current.Session;
                if (ReferenceEquals(Selected, presented)) RenderSelected();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteInputAsync(PresentedSession presented, byte[] bytes)
    {
        if (service is null || bytes.Length == 0 ||
            presented.View.State != DeveloperTerminalSessionState.Running)
        {
            return;
        }

        DeveloperTerminalSessionResult result = await service.WriteAsync(
            presented.View.Id,
            new(bytes),
            presented.Cancellation.Token);
        if (result.Error is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => status.Text = result.Error);
        }
    }

    private void QueueResize(PresentedSession presented, int columns, int rows)
    {
        presented.ResizeCancellation?.Cancel();
        presented.ResizeCancellation?.Dispose();
        presented.ResizeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            presented.Cancellation.Token);
        CancellationToken token = presented.ResizeCancellation.Token;
        _ = ResizeAsync(presented, Math.Clamp(columns, 20, 400), Math.Clamp(rows, 5, 200), token);
    }

    private async Task ResizeAsync(
        PresentedSession presented,
        int columns,
        int rows,
        CancellationToken cancellationToken)
    {
        if (service is null || presented.View.State != DeveloperTerminalSessionState.Running)
        {
            return;
        }

        try
        {
            await Task.Delay(80, cancellationToken);
            DeveloperTerminalSessionResult result = await service.ResizeAsync(
                presented.View.Id,
                new(columns, rows),
                cancellationToken);
            if (result.Session is not null) presented.View = result.Session;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RenderSelected()
    {
        PresentedSession? selected = Selected;
        if (selected is null)
        {
            terminalHost.Content = new TextBlock { Text = "Start a terminal to open an interactive shell." };
            metadata.Text = string.Empty;
            stop.IsEnabled = close.IsEnabled = copy.IsEnabled = paste.IsEnabled = false;
            links.ItemsSource = Array.Empty<UriChoice>();
            links.IsEnabled = false;
            UpdateAvailability();
            return;
        }

        terminalHost.Content = selected.Control;
        DeveloperTerminalSessionView view = selected.View;
        metadata.Text = $"{view.SourceContext.Description} · Working directory {view.WorkingDirectory.Value} · " +
                        $"Shell {view.Shell.Value} · {view.EnvironmentProfile.Value} · " +
                        $"Trusted: {(view.IsTrusted ? "yes" : "no")} · Content: {view.ContentPolicy.Value}";
        bool running = view.State == DeveloperTerminalSessionState.Running;
        stop.IsEnabled = running;
        close.IsEnabled = !running;
        copy.IsEnabled = true;
        paste.IsEnabled = running;
        links.ItemsSource = selected.DetectedLinks.ToArray();
        links.IsEnabled = selected.DetectedLinks.Count > 0;
        openLink.IsEnabled = links.SelectedItem is UriChoice;
        status.Text = running
            ? "Terminal running. Right-click copies a selection or pastes from the clipboard."
            : $"Terminal {view.State} · exit code {view.ExitCode?.ToString() ?? "not reported"}.";
        SearchSelected();
        selected.Control.Focus();
    }

    private void RefreshSessionChoices(PresentedSession selected)
    {
        SessionChoice[] choices = sessions.Select(item => new SessionChoice(item)).ToArray();
        sessionPicker.ItemsSource = choices;
        sessionPicker.SelectedItem = choices.Single(choice => ReferenceEquals(choice.Session, selected));
    }

    private void CloseSelected()
    {
        PresentedSession? selected = Selected;
        if (selected is null || selected.View.State == DeveloperTerminalSessionState.Running)
        {
            return;
        }

        selected.Cancellation.Cancel();
        selected.ResizeCancellation?.Cancel();
        sessions.Remove(selected);
        if (sessions.All(item => !item.HasSensitiveContent)) ReleaseSensitiveDisplay();
        SessionChoice[] choices = sessions.Select(item => new SessionChoice(item)).ToArray();
        sessionPicker.ItemsSource = choices;
        sessionPicker.SelectedIndex = choices.Length == 0 ? -1 : choices.Length - 1;
        RenderSelected();
    }

    private void SearchSelected()
    {
        PresentedSession? selected = Selected;
        string query = search.Text?.Trim() ?? string.Empty;
        int matches = selected is null || query.Length == 0 ? 0 : selected.Control.Search(query);
        previous.IsEnabled = next.IsEnabled = matches > 0;
        searchStatus.Text = query.Length == 0 ? string.Empty : $"{matches} match(es)";
    }

    private void SelectPreviousSearchResult()
    {
        if (Selected is { } selected) selected.Control.SelectPreviousSearchResult();
    }

    private void SelectNextSearchResult()
    {
        if (Selected is { } selected) selected.Control.SelectNextSearchResult();
    }

    private async Task OpenSelectedLinkAsync()
    {
        if (links.SelectedItem is not UriChoice choice ||
            TopLevel.GetTopLevel(Content)?.Launcher is not { } launcher)
        {
            return;
        }

        bool opened = await launcher.LaunchUriAsync(choice.Uri);
        status.Text = opened ? "Opened the selected terminal link." : "The link could not be opened.";
    }

    private void DetectLinks(PresentedSession presented, byte[] bytes)
    {
        string fragment = presented.LinkTail + Encoding.UTF8.GetString(bytes);
        foreach (Match match in WebLink().Matches(fragment))
        {
            string value = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}');
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme is "http" or "https" &&
                presented.DetectedLinks.All(item => item.Uri != uri) &&
                presented.DetectedLinks.Count < MaximumDetectedLinks)
            {
                presented.DetectedLinks.Add(new(uri));
            }
        }

        presented.LinkTail = fragment.Length <= 512 ? fragment : fragment[^512..];
        if (ReferenceEquals(Selected, presented))
        {
            links.ItemsSource = presented.DetectedLinks.ToArray();
            links.IsEnabled = presented.DetectedLinks.Count > 0;
        }
    }

    private WorkspaceView? ActiveWorkspace() =>
        state().Workspaces.Registered.FirstOrDefault(item => item.IsActive);

    private void ReleaseSensitiveDisplay()
    {
        sensitiveDisplayLease?.Dispose();
        sensitiveDisplayLease = null;
    }

    private PresentedSession? Selected => (sessionPicker.SelectedItem as SessionChoice)?.Session;

    [GeneratedRegex("https?://[^\\s\\x00-\\x1F<>\\\"']{1,1024}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WebLink();

    private sealed class PresentedSession(
        DeveloperTerminalSessionView view,
        TerminalControlModel model,
        TerminalControl control,
        CancellationTokenSource cancellation,
        bool hasSensitiveContent)
    {
        public DeveloperTerminalSessionView View { get; set; } = view;
        public TerminalControlModel Model { get; } = model;
        public TerminalControl Control { get; } = control;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public bool HasSensitiveContent { get; } = hasSensitiveContent;
        public CancellationTokenSource? ResizeCancellation { get; set; }
        public Task OutputLoop { get; set; } = Task.CompletedTask;
        public List<UriChoice> DetectedLinks { get; } = [];
        public string LinkTail { get; set; } = string.Empty;
    }

    private sealed record SessionChoice(PresentedSession Session)
    {
        public override string ToString() =>
            $"{Session.View.Shell.Value} · {Session.View.State} · {Session.View.StartedAt:HH:mm:ss}";
    }

    private sealed record UriChoice(Uri Uri)
    {
        public override string ToString() => Uri.ToString();
    }
}
