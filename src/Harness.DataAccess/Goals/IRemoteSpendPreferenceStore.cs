namespace Harness.DataAccess.Goals;

public interface IRemoteSpendPreferenceStore
{
    ValueTask<StoredRemoteSpendPreference> GetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredRemoteSpendPreference> SaveAsync(
        StoredRemoteSpendPreference preference,
        CancellationToken cancellationToken = default);
}
