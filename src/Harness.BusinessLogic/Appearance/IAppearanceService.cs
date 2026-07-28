namespace Harness.BusinessLogic.Appearance;

public interface IAppearanceService
{
    ValueTask<AppearanceSnapshot> GetAsync(CancellationToken cancellationToken = default);

    ValueTask<AppearanceSelectionResult> SelectAsync(
        ThemeId themeId,
        CancellationToken cancellationToken = default);
}
