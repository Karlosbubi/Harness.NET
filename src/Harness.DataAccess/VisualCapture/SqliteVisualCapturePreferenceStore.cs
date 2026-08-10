using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.VisualCapture;

internal sealed class SqliteVisualCapturePreferenceStore(
    IApplicationPaths applicationPaths) : IVisualCapturePreferenceStore
{
    public async ValueTask<StoredVisualCapturePreference> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row row = await connection.QuerySingleAsync<Row>(new CommandDefinition("""
            SELECT is_enabled AS IsEnabled,
                   maximum_bytes AS MaximumBytes,
                   retention_days AS RetentionDays,
                   maximum_captures_per_goal AS MaximumCapturesPerGoal,
                   allow_remote_model_access AS AllowRemoteModelAccess
            FROM visual_capture_preferences
            WHERE id = 1;
            """, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    public async ValueTask<StoredVisualCapturePreference> SaveAsync(
        StoredVisualCapturePreference preference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE visual_capture_preferences
            SET is_enabled = @isEnabled,
                maximum_bytes = @maximumBytes,
                retention_days = @retentionDays,
                maximum_captures_per_goal = @maximumCapturesPerGoal,
                allow_remote_model_access = @allowRemoteModelAccess
            WHERE id = 1;
            """, new
        {
            isEnabled = preference.IsEnabled ? 1 : 0,
            maximumBytes = preference.MaximumBytes,
            retentionDays = preference.RetentionDays,
            maximumCapturesPerGoal = preference.MaximumCapturesPerGoal,
            allowRemoteModelAccess = preference.AllowRemoteModelAccess ? 1 : 0,
        }, cancellationToken: cancellationToken));
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
        public long IsEnabled { get; init; }
        public long MaximumBytes { get; init; }
        public int RetentionDays { get; init; }
        public int MaximumCapturesPerGoal { get; init; }
        public long AllowRemoteModelAccess { get; init; }

        internal StoredVisualCapturePreference ToRecord() => new(
            IsEnabled == 1,
            MaximumBytes,
            RetentionDays,
            MaximumCapturesPerGoal,
            AllowRemoteModelAccess == 1);
    }
}
