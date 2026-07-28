namespace Harness.DataAccess.Models;

public sealed record RemoteModelScope(
    string GoalId,
    ProviderPrivacyPolicy PrivacyPolicy,
    RemoteModelRole? Role = null);
