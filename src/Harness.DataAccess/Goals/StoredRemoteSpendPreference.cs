namespace Harness.DataAccess.Goals;

public sealed record StoredRemoteSpendPreference(
    StoredRemoteSpendMode Mode,
    long? CapMicrousd);
