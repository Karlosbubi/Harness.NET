namespace Harness.BusinessLogic.Privacy;

public enum SensitiveDisplayKind
{
    ProjectUserSecret,
    DeveloperTerminal,
}

public sealed record SensitiveDisplayStatus(
    bool IsSensitiveContentVisible,
    SensitiveDisplayKind? VisibleKind,
    int ActiveVisualCaptures);

public interface ISensitiveDisplayLease : IDisposable;

public interface ISensitiveDisplayGuard
{
    SensitiveDisplayStatus Current { get; }

    bool TryBeginSensitiveDisplay(
        SensitiveDisplayKind kind,
        out ISensitiveDisplayLease? lease);

    bool TryBeginVisualCapture(out ISensitiveDisplayLease? lease);
}
