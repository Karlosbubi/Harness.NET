using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.Workspaces;

internal sealed class WorkspaceService(
    IWorkspaceInspector inspector,
    IWorkspaceStore store) : IWorkspaceService
{
    public async ValueTask<WorkspaceResult> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        WorkspaceInspection inspection = await inspector.InspectAsync(path, cancellationToken);
        RegisteredWorkspace? existing = inspection.Error is null
            ? await store.FindByPathAsync(inspection.RootPath, cancellationToken)
            : null;
        return new(existing?.ToView(), inspection.EntryPoints, inspection.Error);
    }

    public async ValueTask<WorkspaceResult> RegisterAsync(
        string path,
        string entryPoint,
        CancellationToken cancellationToken = default)
    {
        WorkspaceInspection inspection = await inspector.InspectAsync(path, cancellationToken);
        if (inspection.Error is not null)
        {
            return new(null, inspection.EntryPoints, inspection.Error);
        }

        string canonicalEntryPoint = Path.GetFullPath(entryPoint);
        if (!inspection.EntryPoints.Contains(canonicalEntryPoint, StringComparer.Ordinal))
        {
            return new(
                null,
                inspection.EntryPoints,
                "The selected entry point is not a tracked .NET solution or project in this repository.");
        }

        RegisteredWorkspace saved = await store.SaveAsync(
            inspection,
            canonicalEntryPoint,
            cancellationToken);
        RegisteredWorkspace workspace = await store.SetActiveAsync(saved.Id, cancellationToken);
        return new(workspace.ToView(), inspection.EntryPoints, Error: null);
    }

    public async ValueTask<WorkspaceResult> SetTrustAsync(
        string workspaceId,
        bool isTrusted,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace workspace = await store.SetTrustAsync(
            workspaceId,
            isTrusted,
            cancellationToken);
        return new(workspace.ToView(), [workspace.EntryPoint], Error: null);
    }

    public async ValueTask<IReadOnlyList<WorkspaceView>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegisteredWorkspace> workspaces = await store.ListAsync(cancellationToken);
        return workspaces.Select(workspace => workspace.ToView()).ToArray();
    }

    public async ValueTask<WorkspaceView?> GetActiveAsync(
        CancellationToken cancellationToken = default) =>
        (await store.GetActiveAsync(cancellationToken))?.ToView();

    public async ValueTask<WorkspaceView> SelectAsync(
        string workspaceId,
        CancellationToken cancellationToken = default) =>
        (await store.SetActiveAsync(workspaceId, cancellationToken)).ToView();

    public async ValueTask<WorkspaceResult> RefreshAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        RegisteredWorkspace? existing = (await store.ListAsync(cancellationToken))
            .SingleOrDefault(workspace => workspace.Id.Equals(workspaceId, StringComparison.Ordinal));
        if (existing is null) return new(null, [], "The workspace is no longer registered.");
        WorkspaceInspection inspection = await inspector.InspectAsync(existing.RootPath, cancellationToken);
        if (inspection.Error is not null)
            return new(existing.ToView(), inspection.EntryPoints, inspection.Error);
        RegisteredWorkspace saved = await store.SaveAsync(
            inspection, existing.EntryPoint, cancellationToken);
        return new(saved.ToView(), inspection.EntryPoints, null);
    }
}

internal static class RegisteredWorkspaceMapping
{
    internal static WorkspaceView ToView(this RegisteredWorkspace workspace) => new(
        workspace.Id,
        workspace.RootPath,
        workspace.Name,
        workspace.EntryPoint,
        workspace.IsTrusted,
        workspace.IsActive,
        workspace.Branch,
        workspace.IsDirty);
}
