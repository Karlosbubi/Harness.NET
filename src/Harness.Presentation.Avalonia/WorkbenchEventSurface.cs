using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Events;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkbenchEventSurface : IDisposable
{
    private readonly WorkbenchEventQueue queue;
    private readonly Action<WorkbenchEventNavigationTarget> navigate;
    private readonly TimeProvider timeProvider;
    private readonly StackPanel cards = new() { Spacing = 8 };
    private readonly Border surface;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

    internal WorkbenchEventSurface(
        Action<WorkbenchEventNavigationTarget> navigate,
        TimeProvider? timeProvider = null,
        int capacity = WorkbenchEventQueue.DefaultCapacity)
    {
        this.navigate = navigate;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        queue = new(capacity);
        surface = new()
        {
            Child = cards,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new(12, 68, 12, 0),
            MaxWidth = 400,
            IsVisible = false,
        };
        AutomationProperties.SetName(surface, "Workbench notifications");
        timer.Tick += OnTimerTick;
        surface.AttachedToVisualTree += (_, _) => timer.Start();
        surface.DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    internal Control Control => surface;

    internal IReadOnlyList<WorkbenchEventNotification> VisibleNotifications => queue.Snapshot();

    internal void Publish(WorkbenchEvent workbenchEvent)
    {
        WorkbenchEventId announced = queue.Publish(workbenchEvent);
        Render(announced);
    }

    internal void Dismiss(WorkbenchEventId id)
    {
        if (queue.Dismiss(id))
        {
            Render();
        }
    }

    internal void Navigate(WorkbenchEventId id)
    {
        WorkbenchEventNotification? notification = queue.Snapshot()
            .FirstOrDefault(candidate => candidate.Event.Id == id);
        if (notification?.Event.NavigationTarget is not { } target)
        {
            return;
        }

        queue.Dismiss(id);
        Render();
        navigate(target);
    }

    internal void Expire(DateTimeOffset now)
    {
        if (queue.Expire(now))
        {
            Render();
        }
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs) =>
        Expire(timeProvider.GetUtcNow());

    private void Render(WorkbenchEventId? announcedId = null)
    {
        cards.Children.Clear();
        foreach (WorkbenchEventNotification notification in queue.Snapshot())
        {
            Control card = BuildCard(notification);
            if (notification.Event.Id == announcedId)
            {
                AutomationProperties.SetLiveSetting(
                    card,
                    notification.Event.Severity is WorkbenchEventSeverity.Error
                        ? AutomationLiveSetting.Assertive
                        : AutomationLiveSetting.Polite);
                Dispatcher.UIThread.Post(() =>
                    AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Off));
            }
            else
            {
                AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Off);
            }

            cards.Children.Add(card);
        }

        surface.IsVisible = cards.Children.Count > 0;
    }

    private Control BuildCard(WorkbenchEventNotification notification)
    {
        WorkbenchEvent workbenchEvent = notification.Event;
        string source = workbenchEvent.Source.ToString();
        string count = notification.Occurrences > 1 ? $" ×{notification.Occurrences}" : string.Empty;
        TextBlock heading = new()
        {
            Text = $"{SeverityGlyph(workbenchEvent.Severity)} {source}{count}",
            FontWeight = FontWeight.SemiBold,
        };
        TextBlock message = new()
        {
            Text = workbenchEvent.Message.Value,
            TextWrapping = TextWrapping.Wrap,
        };
        Button dismiss = new() { Content = "×", Padding = new(8, 2) };
        AutomationProperties.SetName(dismiss, $"Dismiss {source} notification");
        dismiss.Click += (_, _) => Dismiss(workbenchEvent.Id);
        dismiss.KeyDown += (_, args) =>
        {
            if (args.Key is Key.Escape)
            {
                args.Handled = true;
                Dismiss(workbenchEvent.Id);
            }
        };

        Grid header = new() { ColumnDefinitions = new("*,Auto") };
        header.Children.Add(heading);
        Grid.SetColumn(dismiss, 1);
        header.Children.Add(dismiss);
        StackPanel content = new() { Spacing = 5, Children = { header, message } };
        if (workbenchEvent.NavigationTarget is not null)
        {
            Button open = new()
            {
                Content = "Open details",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new(8, 3),
            };
            AutomationProperties.SetName(open, $"Open details for {source} notification");
            open.Click += (_, _) => Navigate(workbenchEvent.Id);
            open.KeyDown += (_, args) =>
            {
                if (args.Key is Key.Escape)
                {
                    args.Handled = true;
                    Dismiss(workbenchEvent.Id);
                }
            };
            content.Children.Add(open);
        }

        Border card = new()
        {
            Child = content,
            Padding = new(12, 9),
            BorderThickness = new(1),
            CornerRadius = new(5),
            Background = Brush(UiThemeColorToken.Raised),
            BorderBrush = Brush(ColorToken(workbenchEvent.Severity)),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 12,
                OffsetY = 3,
                Color = Color.FromArgb(55, 0, 0, 0),
            }),
        };
        AutomationProperties.SetName(
            card,
            $"{source} {workbenchEvent.Severity}: {workbenchEvent.Message.Value}{count}");
        return card;
    }

    private static string SeverityGlyph(WorkbenchEventSeverity severity) => severity switch
    {
        WorkbenchEventSeverity.Information => "ℹ",
        WorkbenchEventSeverity.Success => "✓",
        WorkbenchEventSeverity.Warning => "⚠",
        WorkbenchEventSeverity.Error => "✕",
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static UiThemeColorToken ColorToken(WorkbenchEventSeverity severity) => severity switch
    {
        WorkbenchEventSeverity.Information => UiThemeColorToken.Info,
        WorkbenchEventSeverity.Success => UiThemeColorToken.Success,
        WorkbenchEventSeverity.Warning => UiThemeColorToken.Warning,
        WorkbenchEventSeverity.Error => UiThemeColorToken.Danger,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static IBrush? Brush(UiThemeColorToken token) =>
        Application.Current?.TryFindResource(HarnessThemeResources.Key(token), out object? value)
            is true && value is IBrush brush
            ? brush
            : null;
}
