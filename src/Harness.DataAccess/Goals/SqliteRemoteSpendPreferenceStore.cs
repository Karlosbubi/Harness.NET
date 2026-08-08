using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Goals;

internal sealed class SqliteRemoteSpendPreferenceStore(
    IApplicationPaths applicationPaths) : IRemoteSpendPreferenceStore
{
    public async ValueTask<StoredRemoteSpendPreference> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row row = await connection.QuerySingleAsync<Row>(new CommandDefinition("""
            SELECT mode AS Mode, cap_microusd AS CapMicrousd
            FROM remote_spend_preferences
            WHERE id = 1;
            """, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    public async ValueTask<StoredRemoteSpendPreference> SaveAsync(
        StoredRemoteSpendPreference preference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        DynamicParameters parameters = new();
        parameters.Add("mode", preference.Mode.ToString());
        parameters.Add("capMicrousd", preference.CapMicrousd);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE remote_spend_preferences
            SET mode = @mode, cap_microusd = @capMicrousd
            WHERE id = 1;
            """, parameters, cancellationToken: cancellationToken));
        return await GetAsync(cancellationToken);
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

    private sealed class Row
    {
        public string Mode { get; init; } = string.Empty;
        public long? CapMicrousd { get; init; }

        public StoredRemoteSpendPreference ToRecord() => new(
            Enum.Parse<StoredRemoteSpendMode>(Mode),
            CapMicrousd);
    }
}
