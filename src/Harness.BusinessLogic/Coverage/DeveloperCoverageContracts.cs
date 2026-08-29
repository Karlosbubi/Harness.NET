using System.Collections.Immutable;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Coverage;

public sealed record DeveloperCoverageImportId(string Value);
public sealed record DeveloperCoverageReportPath(string Value);
public sealed record DeveloperCoverageReportHash(string Value);
public sealed record DeveloperCoverageSourceDescription(string Value);
public sealed record DeveloperCoverageProducer(string Value);
public sealed record DeveloperCoverageVersion(string Value);
public sealed record DeveloperCoverageSourcePath(string Value);
public sealed record DeveloperCoverageLineNumber(int Value);
public sealed record DeveloperCoverageHitCount(long Value);

public enum DeveloperCoverageFormat
{
    Cobertura,
}

public sealed record DeveloperCoverageLine(
    DeveloperCoverageSourcePath Path,
    DeveloperCoverageLineNumber Line,
    DeveloperCoverageHitCount Hits);

public sealed record DeveloperCoverageView(
    DeveloperCoverageImportId Id,
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    DeveloperCoverageSourceDescription SourceDescription,
    DeveloperCoverageReportPath ReportPath,
    DeveloperCoverageReportHash ReportHash,
    DeveloperCoverageFormat Format,
    DeveloperCoverageProducer Producer,
    DeveloperCoverageVersion ProducerVersion,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset ImportedAt,
    int UnmappedFileCount,
    bool IsTruncated,
    ImmutableArray<DeveloperCoverageLine> Lines);

public sealed record DeveloperCoverageImportRequest(
    WorkbenchWorkspaceRequest Workspace,
    DeveloperCoverageReportPath ReportPath);

public sealed record DeveloperCoverageResult(
    DeveloperCoverageView? Coverage,
    string? ErrorCode,
    string? Error);

public interface IDeveloperCoverageService
{
    ValueTask<DeveloperCoverageResult> ImportAsync(
        DeveloperCoverageImportRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DeveloperCoverageResult> GetLatestAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default);
}
