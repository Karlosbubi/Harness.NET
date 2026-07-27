namespace Harness.BusinessLogic.Dashboard;

public sealed record ProviderSnapshot(
    string Name,
    string Health,
    string SelectedModel,
    IReadOnlyList<ProviderModel> Models,
    string? Error);
