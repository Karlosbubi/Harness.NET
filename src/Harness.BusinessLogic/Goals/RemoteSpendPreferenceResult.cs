namespace Harness.BusinessLogic.Goals;

public sealed record RemoteSpendPreferenceResult(
    RemoteSpendPreference Preference,
    string? ErrorCode,
    string? Error);
