using Harness.BusinessLogic.Costs;
using Harness.DataAccess.Goals;

namespace Harness.BusinessLogic.Goals;

internal sealed class RemoteSpendPreferenceService(
    IRemoteSpendPreferenceStore store) : IRemoteSpendPreferenceService
{
    public async ValueTask<RemoteSpendPreference> GetAsync(
        CancellationToken cancellationToken = default) =>
        Map(await store.GetAsync(cancellationToken));

    public async ValueTask<RemoteSpendPreferenceResult> UpdateAsync(
        RemoteSpendPreference preference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (preference.Mode is RemoteSpendMode.Capped && preference.Cap?.Value is not > 0)
        {
            return new(await GetAsync(cancellationToken), "invalid_remote_spend_preference",
                "A capped default requires a positive USD amount.");
        }

        if (preference.Mode is not RemoteSpendMode.Capped && preference.Cap is not null)
        {
            return new(await GetAsync(cancellationToken), "invalid_remote_spend_preference",
                "Only capped remote spending accepts a dollar amount.");
        }

        StoredRemoteSpendPreference saved = await store.SaveAsync(new(
            Map(preference.Mode),
            preference.Cap?.Value), cancellationToken);
        return new(Map(saved), ErrorCode: null, Error: null);
    }

    private static RemoteSpendPreference Map(StoredRemoteSpendPreference value) => new(
        value.Mode switch
        {
            StoredRemoteSpendMode.Unlimited => RemoteSpendMode.Unlimited,
            StoredRemoteSpendMode.Capped => RemoteSpendMode.Capped,
            StoredRemoteSpendMode.LocalOnly => RemoteSpendMode.LocalOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        },
        value.CapMicrousd is null ? null : new(value.CapMicrousd.Value));

    private static StoredRemoteSpendMode Map(RemoteSpendMode value) => value switch
    {
        RemoteSpendMode.Unlimited => StoredRemoteSpendMode.Unlimited,
        RemoteSpendMode.Capped => StoredRemoteSpendMode.Capped,
        RemoteSpendMode.LocalOnly => StoredRemoteSpendMode.LocalOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
