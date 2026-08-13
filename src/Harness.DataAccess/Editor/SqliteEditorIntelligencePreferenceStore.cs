using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Editor;

internal sealed class SqliteEditorIntelligencePreferenceStore(
    IApplicationPaths applicationPaths) : IEditorIntelligencePreferenceStore
{
    public async ValueTask<StoredEditorIntelligencePreferences> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        Row row = await connection.QuerySingleAsync<Row>(new CommandDefinition("""
            SELECT show_parameter_name_hints AS ShowParameterNameHints,
                   show_inferred_type_hints AS ShowInferredTypeHints,
                   show_reference_code_lens AS ShowReferenceCodeLens,
                   show_implementation_code_lens AS ShowImplementationCodeLens,
                   show_test_code_lens AS ShowTestCodeLens,
                   show_run_code_lens AS ShowRunCodeLens,
                   show_debug_code_lens AS ShowDebugCodeLens,
                   format_on_paste AS FormatOnPaste,
                   format_on_type AS FormatOnType
            FROM editor_intelligence_preferences
            WHERE id = 1;
            """, cancellationToken: cancellationToken));
        return row.ToRecord();
    }

    public async ValueTask<StoredEditorIntelligencePreferences> SaveAsync(
        StoredEditorIntelligencePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE editor_intelligence_preferences
            SET show_parameter_name_hints = @parameterNames,
                show_inferred_type_hints = @inferredTypes,
                show_reference_code_lens = @references,
                show_implementation_code_lens = @implementations,
                show_test_code_lens = @tests,
                show_run_code_lens = @run,
                show_debug_code_lens = @debug,
                format_on_paste = @formatOnPaste,
                format_on_type = @formatOnType
            WHERE id = 1;
            """, new
        {
            parameterNames = preferences.ShowParameterNameHints ? 1 : 0,
            inferredTypes = preferences.ShowInferredTypeHints ? 1 : 0,
            references = preferences.ShowReferenceCodeLens ? 1 : 0,
            implementations = preferences.ShowImplementationCodeLens ? 1 : 0,
            tests = preferences.ShowTestCodeLens ? 1 : 0,
            run = preferences.ShowRunCodeLens ? 1 : 0,
            debug = preferences.ShowDebugCodeLens ? 1 : 0,
            formatOnPaste = preferences.FormatOnPaste ? 1 : 0,
            formatOnType = preferences.FormatOnType ? 1 : 0,
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
        public long ShowParameterNameHints { get; init; }
        public long ShowInferredTypeHints { get; init; }
        public long ShowReferenceCodeLens { get; init; }
        public long ShowImplementationCodeLens { get; init; }
        public long ShowTestCodeLens { get; init; }
        public long ShowRunCodeLens { get; init; }
        public long ShowDebugCodeLens { get; init; }
        public long FormatOnPaste { get; init; }
        public long FormatOnType { get; init; }

        internal StoredEditorIntelligencePreferences ToRecord() => new(
            ShowParameterNameHints == 1,
            ShowInferredTypeHints == 1,
            ShowReferenceCodeLens == 1,
            ShowImplementationCodeLens == 1,
            ShowTestCodeLens == 1,
            FormatOnPaste == 1,
            FormatOnType == 1,
            ShowRunCodeLens == 1,
            ShowDebugCodeLens == 1);
    }
}
