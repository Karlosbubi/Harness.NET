namespace Harness.BusinessLogic.Events;

public sealed record WorkbenchEventId(string Value);

public sealed record WorkbenchEventMessage
{
    public const int MaximumLength = 240;

    public WorkbenchEventMessage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value.Length,
                $"Workbench event messages cannot exceed {MaximumLength} characters.");
        }

        Value = value;
    }

    public string Value { get; }
}

public enum WorkbenchEventSeverity
{
    Information,
    Success,
    Warning,
    Error,
}

public enum WorkbenchEventSource
{
    Application,
    Goal,
    Git,
    Indexing,
    Execution,
    Backup,
}

public enum WorkbenchEventNavigationTarget
{
    Conversation,
    Git,
    RunOutput,
    Problems,
    Operations,
}

public sealed record WorkbenchEvent(
    WorkbenchEventId Id,
    WorkbenchEventSeverity Severity,
    WorkbenchEventSource Source,
    WorkbenchEventMessage Message,
    DateTimeOffset OccurredAt,
    WorkbenchEventNavigationTarget? NavigationTarget = null);
