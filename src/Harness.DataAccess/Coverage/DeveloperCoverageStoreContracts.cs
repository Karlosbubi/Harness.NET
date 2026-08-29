using System.Collections.Immutable;

namespace Harness.DataAccess.Coverage;

public sealed record StoredCoverageImportId(string Value);
public sealed record StoredCoverageWorkspaceId(string Value);
public sealed record StoredCoverageGoalId(string Value);
public sealed record StoredCoverageSourceDescription(string Value);

public sealed record StoredCoverageImport(
    StoredCoverageImportId Id,
    StoredCoverageWorkspaceId WorkspaceId,
    StoredCoverageGoalId? GoalId,
    StoredCoverageSourceDescription SourceDescription,
    CoverageReportPath ReportPath,
    CoverageReportHash ReportHash,
    CoverageReportFormat Format,
    CoverageProducerName Producer,
    CoverageProducerVersion ProducerVersion,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset ImportedAt,
    int UnmappedFileCount,
    bool IsTruncated,
    ImmutableArray<CoverageLineRecord> Lines);

public interface IDeveloperCoverageStore
{
    ValueTask SaveAsync(
        StoredCoverageImport coverage,
        CancellationToken cancellationToken = default);

    ValueTask<StoredCoverageImport?> GetLatestAsync(
        StoredCoverageWorkspaceId workspaceId,
        StoredCoverageGoalId? goalId,
        CancellationToken cancellationToken = default);
}
