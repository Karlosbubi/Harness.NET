namespace Harness.BusinessLogic.Goals;

public interface IRemoteSpendPreferenceService
{
    ValueTask<RemoteSpendPreference> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<RemoteSpendPreferenceResult> UpdateAsync(
        RemoteSpendPreference preference,
        CancellationToken cancellationToken = default);
}
