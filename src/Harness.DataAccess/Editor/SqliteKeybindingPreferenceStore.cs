using System.Data.Common;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Editor;

internal sealed class SqliteKeybindingPreferenceStore(
    IApplicationPaths applicationPaths) : IKeybindingPreferenceStore
{
    private const int MaximumBindings = 128;

    public async ValueTask<StoredKeybindingPreferences> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        bool useDefaults = await connection.QuerySingleAsync<long>(new CommandDefinition("""
            SELECT use_defaults FROM keybinding_configuration WHERE id = 1;
            """, cancellationToken: cancellationToken)) == 1;
        Row[] rows = (await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT command_name AS CommandName,
                   position AS Position,
                   gesture_text AS GestureText
            FROM keybinding_preferences
            ORDER BY command_name COLLATE BINARY, position;
            """, cancellationToken: cancellationToken))).ToArray();
        if (rows.Length > MaximumBindings)
        {
            throw new InvalidDataException("Stored keybindings exceed the supported limit.");
        }

        return new(useDefaults, rows.Select(row => row.ToRecord()).ToArray());
    }

    public async ValueTask<StoredKeybindingPreferences> SaveAsync(
        StoredKeybindingPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Validate(preferences);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM keybinding_preferences;", transaction: transaction,
            cancellationToken: cancellationToken));
        foreach (StoredKeybinding binding in preferences.Bindings)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO keybinding_preferences (command_name, position, gesture_text)
                VALUES (@CommandName, @Position, @GestureText);
                """, new
            {
                CommandName = binding.Command.Value,
                binding.Position,
                GestureText = binding.Gesture.Value,
            }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE keybinding_configuration SET use_defaults = @UseDefaults WHERE id = 1;
            """, new { UseDefaults = preferences.UseDefaults ? 1 : 0 }, transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public ValueTask<StoredKeybindingPreferences> ResetAsync(
        CancellationToken cancellationToken = default) =>
        SaveAsync(new(true, []), cancellationToken);

    private static void Validate(StoredKeybindingPreferences preferences)
    {
        if (preferences.Bindings.Count > MaximumBindings ||
            preferences.UseDefaults && preferences.Bindings.Count != 0)
        {
            throw new InvalidDataException("Keybinding settings are inconsistent or too large.");
        }

        foreach (StoredKeybinding binding in preferences.Bindings)
        {
            if (binding.Command.Value.Length is < 1 or > 80 ||
                binding.Gesture.Value.Length is < 1 or > 80 ||
                binding.Position is < 0 or > 7 ||
                binding.Command.Value.Any(char.IsControl) ||
                binding.Gesture.Value.Any(char.IsControl))
            {
                throw new InvalidDataException("A stored keybinding is invalid.");
            }
        }

        if (preferences.Bindings.GroupBy(binding => (binding.Command.Value, binding.Position))
            .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException("Stored keybinding positions must be unique.");
        }
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
        public required string CommandName { get; init; }
        public int Position { get; init; }
        public required string GestureText { get; init; }

        internal StoredKeybinding ToRecord() => new(
            new(CommandName), Position, new(GestureText));
    }
}
