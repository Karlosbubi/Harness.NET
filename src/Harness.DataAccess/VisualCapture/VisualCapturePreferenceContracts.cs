namespace Harness.DataAccess.VisualCapture;

public sealed record StoredVisualCapturePreference(
    bool IsEnabled,
    long MaximumBytes,
    int RetentionDays,
    int MaximumCapturesPerGoal,
    bool AllowRemoteModelAccess);

public interface IVisualCapturePreferenceStore
{
    ValueTask<StoredVisualCapturePreference> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredVisualCapturePreference> SaveAsync(
        StoredVisualCapturePreference preference,
        CancellationToken cancellationToken = default);
}
