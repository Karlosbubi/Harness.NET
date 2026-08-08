using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.DataAccess.Goals;

namespace Harness.BusinessLogic.Tests.Goals;

public sealed class RemoteSpendPreferenceServiceTests
{
    [Fact]
    public async Task Defaults_to_unlimited_and_persists_an_explicit_cap()
    {
        PreferenceStore store = new(new(StoredRemoteSpendMode.Unlimited, CapMicrousd: null));
        RemoteSpendPreferenceService service = new(store);

        RemoteSpendPreference initial = await service.GetAsync();
        RemoteSpendPreferenceResult saved = await service.UpdateAsync(new(
            RemoteSpendMode.Capped,
            new MicroUsdAmount(2_500_000)));

        Assert.Equal(RemoteSpendMode.Unlimited, initial.Mode);
        Assert.Equal(long.MaxValue, initial.ToGoalBudget()?.Value);
        Assert.Null(saved.Error);
        Assert.Equal(2_500_000, saved.Preference.Cap?.Value);
        Assert.Equal(StoredRemoteSpendMode.Capped, store.Value.Mode);
    }

    [Fact]
    public async Task Rejects_a_capped_mode_without_a_positive_cap()
    {
        RemoteSpendPreferenceService service = new(new PreferenceStore(
            new(StoredRemoteSpendMode.Unlimited, CapMicrousd: null)));

        RemoteSpendPreferenceResult result = await service.UpdateAsync(new(
            RemoteSpendMode.Capped,
            Cap: null));

        Assert.Equal("invalid_remote_spend_preference", result.ErrorCode);
        Assert.Equal(RemoteSpendMode.Unlimited, result.Preference.Mode);
    }

    private sealed class PreferenceStore(StoredRemoteSpendPreference value)
        : IRemoteSpendPreferenceStore
    {
        internal StoredRemoteSpendPreference Value { get; private set; } = value;

        public ValueTask<StoredRemoteSpendPreference> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Value);

        public ValueTask<StoredRemoteSpendPreference> SaveAsync(
            StoredRemoteSpendPreference preference,
            CancellationToken cancellationToken = default)
        {
            Value = preference;
            return ValueTask.FromResult(Value);
        }
    }
}
