using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Harness.BusinessLogic.Events;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class WorkbenchEventSurfaceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-28T20:00:00Z");

    [Fact]
    public void Queue_coalesces_repeated_events_and_moves_them_to_the_newest_position()
    {
        WorkbenchEventQueue queue = new(3);
        queue.Publish(Event("one", "First"));
        queue.Publish(Event("two", "Second"));

        WorkbenchEventId id = queue.Publish(Event("repeat", "First", Now.AddSeconds(1)));

        Assert.Equal("one", id.Value);
        Assert.Collection(
            queue.Snapshot(),
            item => Assert.Equal("two", item.Event.Id.Value),
            item =>
            {
                Assert.Equal("one", item.Event.Id.Value);
                Assert.Equal(2, item.Occurrences);
                Assert.Equal(Now.AddSeconds(1), item.Event.OccurredAt);
            });
    }

    [Fact]
    public void Queue_evicts_oldest_and_expires_by_typed_severity_without_a_timer()
    {
        WorkbenchEventQueue queue = new(2);
        queue.Publish(Event("old", "Old", Now.AddSeconds(-9)));
        queue.Publish(Event(
            "error", "Failure", Now.AddSeconds(-9), WorkbenchEventSeverity.Error));
        queue.Publish(Event("new", "New"));

        Assert.DoesNotContain(queue.Snapshot(), item => item.Event.Id.Value == "old");
        Assert.False(queue.Expire(Now.AddSeconds(7)));
        Assert.True(queue.Expire(Now.AddSeconds(9)));
        Assert.Single(queue.Snapshot());
        Assert.Equal("error", queue.Snapshot()[0].Event.Id.Value);
    }

    [Fact]
    public void Message_rejects_empty_and_unbounded_content()
    {
        Assert.Throws<ArgumentException>(() => new WorkbenchEventMessage(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkbenchEventMessage(new string('x', WorkbenchEventMessage.MaximumLength + 1)));
    }

    [Fact]
    public async Task Surface_is_non_modal_navigable_dismissible_and_announces_only_on_publish()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        WorkbenchEventSurface? surface = null;
        Border? card = null;
        WorkbenchEventNavigationTarget? navigated = null;
        await session.Dispatch(() =>
        {
            surface = new(target => navigated = target);
            surface.Publish(Event(
                "goal", "Plan completed", navigation: WorkbenchEventNavigationTarget.Conversation));

            Border host = Assert.IsType<Border>(surface.Control);
            Assert.True(host.IsVisible);
            StackPanel cards = Assert.IsType<StackPanel>(host.Child);
            card = Assert.IsType<Border>(Assert.Single(cards.Children));
            Assert.Equal(AutomationLiveSetting.Polite,
                AutomationProperties.GetLiveSetting(card));
            Assert.Contains("Goal Success", AutomationProperties.GetName(card),
                StringComparison.Ordinal);

            surface.Expire(Now);
            Assert.Same(card, Assert.Single(cards.Children));

            surface.Navigate(new("goal"));
            Assert.Equal(WorkbenchEventNavigationTarget.Conversation, navigated);
            Assert.Empty(surface.VisibleNotifications);

            surface.Publish(Event("warning", "Review this", severity: WorkbenchEventSeverity.Warning));
            Border warningCard = Assert.IsType<Border>(Assert.Single(cards.Children));
            StackPanel content = Assert.IsType<StackPanel>(warningCard.Child);
            Grid heading = Assert.IsType<Grid>(content.Children[0]);
            Button dismiss = Assert.IsType<Button>(heading.Children[1]);
            dismiss.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });

            Assert.Empty(surface.VisibleNotifications);
            Assert.False(host.IsVisible);
            surface.Dispose();
        }, CancellationToken.None);
    }

    private static WorkbenchEvent Event(
        string id,
        string message,
        DateTimeOffset? occurredAt = null,
        WorkbenchEventSeverity severity = WorkbenchEventSeverity.Success,
        WorkbenchEventNavigationTarget? navigation = null) =>
        new(
            new(id),
            severity,
            WorkbenchEventSource.Goal,
            new(message),
            occurredAt ?? Now,
            navigation);
}
