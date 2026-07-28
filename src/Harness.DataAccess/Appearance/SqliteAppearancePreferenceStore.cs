using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Appearance;

internal sealed class SqliteAppearancePreferenceStore(IApplicationPaths applicationPaths)
    : IAppearancePreferenceStore
{
    public async ValueTask<ThemeId> GetSelectedThemeAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        string value = await connection.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT selected_theme_id
            FROM appearance_preferences
            WHERE id = 1;
            """, cancellationToken: cancellationToken));
        return new(value);
    }

    public async ValueTask SaveSelectedThemeAsync(
        ThemeId themeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(themeId);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE appearance_preferences
            SET selected_theme_id = @themeId
            WHERE id = 1;
            """, new { themeId = themeId.Value }, cancellationToken: cancellationToken));
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = applicationPaths.Current.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
