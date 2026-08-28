using Harness.BusinessLogic.Events;

namespace Harness.Presentation.Avalonia;

internal sealed record WorkbenchEventNotification(
    WorkbenchEvent Event,
    int Occurrences);

internal sealed class WorkbenchEventQueue
{
    internal const int DefaultCapacity = 4;
    private const int MaximumCapacity = 16;
    private readonly int capacity;
    private readonly List<WorkbenchEventNotification> notifications = [];

    internal WorkbenchEventQueue(int capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
    }

    internal WorkbenchEventId Publish(WorkbenchEvent workbenchEvent)
    {
        ArgumentNullException.ThrowIfNull(workbenchEvent);
        int existingIndex = notifications.FindIndex(notification =>
            SameSemanticEvent(notification.Event, workbenchEvent));
        if (existingIndex >= 0)
        {
            WorkbenchEventNotification existing = notifications[existingIndex];
            WorkbenchEvent coalesced = workbenchEvent with { Id = existing.Event.Id };
            notifications.RemoveAt(existingIndex);
            notifications.Add(new(coalesced, existing.Occurrences + 1));
            return existing.Event.Id;
        }

        if (notifications.Count == capacity)
        {
            notifications.RemoveAt(0);
        }

        notifications.Add(new(workbenchEvent, 1));
        return workbenchEvent.Id;
    }

    internal bool Dismiss(WorkbenchEventId id)
    {
        int index = notifications.FindIndex(notification => notification.Event.Id == id);
        if (index < 0)
        {
            return false;
        }

        notifications.RemoveAt(index);
        return true;
    }

    internal bool Expire(DateTimeOffset now)
    {
        int removed = notifications.RemoveAll(notification =>
            now - notification.Event.OccurredAt >= Lifetime(notification.Event.Severity));
        return removed > 0;
    }

    internal IReadOnlyList<WorkbenchEventNotification> Snapshot() =>
        notifications.ToArray();

    internal static TimeSpan Lifetime(WorkbenchEventSeverity severity) => severity switch
    {
        WorkbenchEventSeverity.Information => TimeSpan.FromSeconds(8),
        WorkbenchEventSeverity.Success => TimeSpan.FromSeconds(8),
        WorkbenchEventSeverity.Warning => TimeSpan.FromSeconds(15),
        WorkbenchEventSeverity.Error => TimeSpan.FromSeconds(30),
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static bool SameSemanticEvent(WorkbenchEvent left, WorkbenchEvent right) =>
        left.Severity == right.Severity &&
        left.Source == right.Source &&
        left.Message == right.Message &&
        left.NavigationTarget == right.NavigationTarget;
}
