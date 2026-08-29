using System.Collections.Immutable;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Coverage;

internal sealed class SqliteDeveloperCoverageStore(
    IApplicationPaths applicationPaths) : IDeveloperCoverageStore
{
    private const int MaximumImportsPerContext = 10;

    public async ValueTask SaveAsync(
        StoredCoverageImport coverage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO developer_coverage_imports (
                id, workspace_id, goal_id, source_description, report_path, report_hash,
                format, producer, producer_version, generated_at, imported_at,
                unmapped_file_count, is_truncated)
            VALUES (
                @id, @workspaceId, @goalId, @sourceDescription, @reportPath, @reportHash,
                @format, @producer, @producerVersion, @generatedAt, @importedAt,
                @unmappedFileCount, @isTruncated);
            """, new
        {
            id = coverage.Id.Value,
            workspaceId = coverage.WorkspaceId.Value,
            goalId = coverage.GoalId?.Value,
            sourceDescription = coverage.SourceDescription.Value,
            reportPath = coverage.ReportPath.Value,
            reportHash = coverage.ReportHash.Value,
            format = coverage.Format.ToString(),
            producer = coverage.Producer.Value,
            producerVersion = coverage.ProducerVersion.Value,
            generatedAt = coverage.GeneratedAt?.ToString("O"),
            importedAt = coverage.ImportedAt.ToString("O"),
            unmappedFileCount = coverage.UnmappedFileCount,
            isTruncated = coverage.IsTruncated ? 1 : 0,
        }, transaction, cancellationToken: cancellationToken));
        if (!coverage.Lines.IsDefaultOrEmpty)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO developer_coverage_lines (
                    import_id, source_path, line_number, hit_count)
                VALUES (@importId, @sourcePath, @lineNumber, @hitCount);
                """, coverage.Lines.Select(line => new
                {
                    importId = coverage.Id.Value,
                    sourcePath = line.Path.Value,
                    lineNumber = line.Line.Value,
                    hitCount = line.Hits.Value,
                }), transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM developer_coverage_imports
            WHERE id IN (
                SELECT id
                FROM developer_coverage_imports
                WHERE workspace_id = @workspaceId
                  AND ((@goalId IS NULL AND goal_id IS NULL) OR goal_id = @goalId)
                ORDER BY imported_at DESC, rowid DESC
                LIMIT -1 OFFSET @maximumImports);
            """, new
        {
            workspaceId = coverage.WorkspaceId.Value,
            goalId = coverage.GoalId?.Value,
            maximumImports = MaximumImportsPerContext,
        }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<StoredCoverageImport?> GetLatestAsync(
        StoredCoverageWorkspaceId workspaceId,
        StoredCoverageGoalId? goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        ImportRow? import = await connection.QuerySingleOrDefaultAsync<ImportRow>(
            new CommandDefinition("""
                SELECT id AS Id,
                       workspace_id AS WorkspaceId,
                       goal_id AS GoalId,
                       source_description AS SourceDescription,
                       report_path AS ReportPath,
                       report_hash AS ReportHash,
                       format AS Format,
                       producer AS Producer,
                       producer_version AS ProducerVersion,
                       generated_at AS GeneratedAt,
                       imported_at AS ImportedAt,
                       unmapped_file_count AS UnmappedFileCount,
                       is_truncated AS IsTruncated
                FROM developer_coverage_imports
                WHERE workspace_id = @workspaceId
                  AND ((@goalId IS NULL AND goal_id IS NULL) OR goal_id = @goalId)
                ORDER BY imported_at DESC, rowid DESC
                LIMIT 1;
                """, new { workspaceId = workspaceId.Value, goalId = goalId?.Value },
                cancellationToken: cancellationToken));
        if (import is null) return null;
        LineRow[] lines = (await connection.QueryAsync<LineRow>(new CommandDefinition("""
            SELECT source_path AS SourcePath,
                   line_number AS LineNumber,
                   hit_count AS HitCount
            FROM developer_coverage_lines
            WHERE import_id = @id
            ORDER BY source_path, line_number;
            """, new { id = import.Id }, cancellationToken: cancellationToken))).ToArray();
        return import.ToRecord(lines.Select(line => line.ToRecord()).ToImmutableArray());
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

    private sealed class ImportRow
    {
        public string Id { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string? GoalId { get; init; }
        public string SourceDescription { get; init; } = string.Empty;
        public string ReportPath { get; init; } = string.Empty;
        public string ReportHash { get; init; } = string.Empty;
        public string Format { get; init; } = string.Empty;
        public string Producer { get; init; } = string.Empty;
        public string ProducerVersion { get; init; } = string.Empty;
        public string? GeneratedAt { get; init; }
        public string ImportedAt { get; init; } = string.Empty;
        public long UnmappedFileCount { get; init; }
        public long IsTruncated { get; init; }

        internal StoredCoverageImport ToRecord(
            ImmutableArray<CoverageLineRecord> lines) => new(
            new(Id), new(WorkspaceId), GoalId is null ? null : new(GoalId),
            new(SourceDescription), new(ReportPath), new(ReportHash),
            Enum.Parse<CoverageReportFormat>(Format), new(Producer), new(ProducerVersion),
            GeneratedAt is null ? null : DateTimeOffset.Parse(
                GeneratedAt, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(ImportedAt, System.Globalization.CultureInfo.InvariantCulture),
            checked((int)UnmappedFileCount),
            IsTruncated != 0, lines);
    }

    private sealed class LineRow
    {
        public string SourcePath { get; init; } = string.Empty;
        public long LineNumber { get; init; }
        public long HitCount { get; init; }

        internal CoverageLineRecord ToRecord() => new(
            new(SourcePath), new(checked((int)LineNumber)), new(HitCount));
    }
}
