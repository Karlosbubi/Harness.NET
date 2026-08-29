using System.Collections.Immutable;

namespace Harness.DataAccess.Coverage;

public sealed record CoverageReportPath(string Value);
public sealed record CoverageReportHash(string Value);
public sealed record CoverageProducerName(string Value);
public sealed record CoverageProducerVersion(string Value);
public sealed record CoverageSourcePath(string Value);
public sealed record CoverageLineNumber(int Value);
public sealed record CoverageHitCount(long Value);

public enum CoverageReportFormat
{
    Cobertura,
}

public sealed record CoverageLineRecord(
    CoverageSourcePath Path,
    CoverageLineNumber Line,
    CoverageHitCount Hits);

public sealed record WorkspaceCoverageReadResult(
    CoverageReportPath ReportPath,
    CoverageReportHash? ReportHash,
    CoverageReportFormat Format,
    CoverageProducerName? Producer,
    CoverageProducerVersion? ProducerVersion,
    DateTimeOffset? GeneratedAt,
    ImmutableArray<CoverageLineRecord> Lines,
    int UnmappedFileCount,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public interface IWorkspaceCoverageReader
{
    ValueTask<WorkspaceCoverageReadResult> ReadAsync(
        string workspaceRoot,
        CoverageReportPath reportPath,
        CancellationToken cancellationToken = default);
}
