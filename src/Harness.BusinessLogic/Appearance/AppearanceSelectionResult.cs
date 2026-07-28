namespace Harness.BusinessLogic.Appearance;

public sealed record AppearanceSelectionResult(
    AppearanceSnapshot Snapshot,
    bool WasSelected,
    string? Error);
