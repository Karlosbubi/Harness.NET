namespace Harness.DataAccess.Appearance;

public interface IUserThemeSource
{
    ValueTask<UserThemeCatalog> ReadAsync(CancellationToken cancellationToken = default);
}
