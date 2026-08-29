using System.Collections.Immutable;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Coverage;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Coverage;

internal sealed class DeveloperCoverageService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IWorkspaceCoverageReader reader,
    IDeveloperCoverageStore store,
    TimeProvider timeProvider,
    ILogger<DeveloperCoverageService> logger) : IDeveloperCoverageService
{
    public async ValueTask<DeveloperCoverageResult> ImportAsync(
        DeveloperCoverageImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReportPath is null || string.IsNullOrWhiteSpace(request.ReportPath.Value) ||
            request.ReportPath.Value.Length > 1_024 ||
            !request.ReportPath.Value.Equals(request.ReportPath.Value.Trim(),
                StringComparison.Ordinal))
            return new(null, "coverage_path_invalid",
                "Enter a bounded workspace-relative Cobertura XML path.");
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request.Workspace, cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null)
            return new(null, resolution.ErrorCode, resolution.Error);
        WorkspaceCoverageReadResult read = await reader.ReadAsync(
            resolution.RootPath, new(request.ReportPath.Value), cancellationToken);
        if (read.Error is not null || read.ReportHash is null || read.Producer is null ||
            read.ProducerVersion is null)
            return new(null, read.ErrorCode, read.Error);

        StoredCoverageImport imported = new(
            new(Guid.NewGuid().ToString("N")),
            new(request.Workspace.WorkspaceId.Value),
            resolution.Context.GoalId is null ? null : new(resolution.Context.GoalId.Value),
            new(resolution.Context.Description),
            read.ReportPath, read.ReportHash, read.Format, read.Producer,
            read.ProducerVersion, read.GeneratedAt, timeProvider.GetUtcNow(),
            read.UnmappedFileCount, read.IsTruncated, read.Lines);
        try
        {
            await store.SaveAsync(imported, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist coverage import {CoverageId}.",
                imported.Id.Value);
            return new(null, "coverage_state_unavailable",
                "The coverage report was valid but could not be recorded safely.");
        }
        return new(Map(imported), null, null);
    }

    public async ValueTask<DeveloperCoverageResult> GetLatestAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request, cancellationToken);
        if (resolution.Error is not null)
            return new(null, resolution.ErrorCode, resolution.Error);
        try
        {
            StoredCoverageImport? coverage = await store.GetLatestAsync(
                new(request.WorkspaceId.Value),
                resolution.Context.GoalId is null ? null : new(resolution.Context.GoalId.Value),
                cancellationToken);
            return new(coverage is null ? null : Map(coverage), null, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not load coverage for workspace {WorkspaceId}.",
                request.WorkspaceId.Value);
            return new(null, "coverage_state_unavailable",
                "Coverage history is unavailable.");
        }
    }

    private static DeveloperCoverageView Map(StoredCoverageImport coverage) => new(
        new(coverage.Id.Value), new(coverage.WorkspaceId.Value),
        coverage.GoalId is null ? null : new(coverage.GoalId.Value),
        new(coverage.SourceDescription.Value),
        new(coverage.ReportPath.Value), new(coverage.ReportHash.Value),
        coverage.Format switch
        {
            CoverageReportFormat.Cobertura => DeveloperCoverageFormat.Cobertura,
            _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
        },
        new(coverage.Producer.Value), new(coverage.ProducerVersion.Value),
        coverage.GeneratedAt, coverage.ImportedAt, coverage.UnmappedFileCount,
        coverage.IsTruncated,
        coverage.Lines.Select(line => new DeveloperCoverageLine(
            new(line.Path.Value), new(line.Line.Value), new(line.Hits.Value)))
            .ToImmutableArray());
}
