using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Inspection;

internal sealed class WorkbenchInspectionService(
    IWorkbenchWorkspaceContextResolver contextResolver,
    IWorkspaceFileCatalogReader fileCatalogReader,
    IWorkspaceTextSearcher textSearcher,
    IWorkspaceGitInspector gitInspector,
    IWorkspaceDotNetInspector dotNetInspector) : IWorkbenchInspectionService
{
    public async ValueTask<WorkbenchFileCatalogResult> ListFilesAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request,
            cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null)
        {
            return new(
                resolution.Context,
                new(
                    [],
                    IsTruncated: false,
                    resolution.ErrorCode ?? "workspace_unavailable",
                    resolution.Error ?? "The workspace context is unavailable."));
        }

        WorkspaceFileCatalog catalog = await fileCatalogReader.ReadAsync(
            resolution.RootPath,
            cancellationToken);
        return new(
            resolution.Context,
            new(
                catalog.Files.Select(file => new WorkbenchDocumentPath(file.Value)).ToArray(),
                catalog.IsTruncated,
                catalog.ErrorCode,
                catalog.Error));
    }

    public async ValueTask<WorkbenchTextSearchResult> SearchTextAsync(
        WorkbenchWorkspaceRequest request,
        string query,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request,
            cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null)
        {
            return new(
                resolution.Context,
                new(
                    [],
                    0,
                    IsTruncated: false,
                    resolution.ErrorCode ?? "workspace_unavailable",
                    resolution.Error ?? "The workspace context is unavailable."));
        }

        WorkspaceTextSearch search = await textSearcher.SearchAsync(
            resolution.RootPath,
            query,
            cancellationToken);
        return new(
            resolution.Context,
            new(
                search.Matches.Select(match => new WorkspaceTextMatchView(
                    match.Path,
                    match.LineNumber,
                    match.Text)).ToArray(),
                search.FilesScanned,
                search.IsTruncated,
                search.ErrorCode,
                search.Error));
    }

    public async ValueTask<WorkbenchGitInspectionResult> InspectGitAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request,
            cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null)
        {
            return new(
                resolution.Context,
                new(
                    string.Empty,
                    null,
                    [],
                    string.Empty,
                    IsTruncated: false,
                    resolution.ErrorCode ?? "workspace_unavailable",
                    resolution.Error ?? "The workspace context is unavailable."));
        }

        WorkspaceGitState git = await gitInspector.InspectAsync(
            resolution.RootPath,
            cancellationToken);
        return new(
            resolution.Context,
            new(
                git.Branch,
                git.HeadSha,
                git.Changes.Select(change => new WorkspaceGitFileChangeView(
                    change.Path,
                    change.Status,
                    change.IndexStatus,
                    change.WorktreeStatus,
                    change.IsStaged,
                    change.IsUnstaged,
                    change.IsConflicted)).ToArray(),
                git.Diff,
                git.IsTruncated,
                git.ErrorCode,
                git.Error,
                git.Fingerprint,
                git.StagedDiff,
                git.UnstagedDiff,
                DeveloperGitService.MapPatchUnits(git.PatchUnits)));
    }

    public async ValueTask<WorkbenchDotNetInspectionResult> InspectDotNetAsync(
        WorkbenchWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkbenchWorkspaceResolution resolution = await contextResolver.ResolveAsync(
            request,
            cancellationToken);
        if (resolution.Error is not null || resolution.RootPath is null ||
            resolution.EntryPoint is null)
        {
            return new(
                resolution.Context,
                new(
                    string.Empty,
                    string.Empty,
                    null,
                    [],
                    IsTruncated: false,
                    resolution.ErrorCode ?? "workspace_unavailable",
                    resolution.Error ?? "The workspace project context is unavailable."));
        }

        WorkspaceDotNetInfo result = await dotNetInspector.InspectAsync(
            resolution.RootPath,
            resolution.EntryPoint.Value,
            cancellationToken);
        return new(
            resolution.Context,
            DotNetInspectionMapper.Map(result));
    }
}
