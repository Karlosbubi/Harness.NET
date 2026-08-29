using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal void SetRepositoryPath(string value) =>
        Publish(Current with
        {
            Workspaces = Current.Workspaces with
            {
                RepositoryPath = value,
                EntryPoints = [],
                Status = null,
            },
        });

    internal void SetWorkspaceStatus(string value) =>
        Publish(Current with
        {
            Workspaces = Current.Workspaces with { Status = value },
        });

    internal async ValueTask RefreshWorkspacesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WorkspaceView> workspaces = await workspaceService.ListAsync(cancellationToken);
            Publish(Current with
            {
                Workspaces = Current.Workspaces with
                {
                    Registered = workspaces,
                    Status = null,
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishWorkspaceFailure(exception, "Workspace refresh");
        }
    }

    internal async ValueTask RefreshActiveWorkspaceContextAsync(CancellationToken cancellationToken)
    {
        WorkspaceView? active = ActiveWorkspace(Current.Workspaces.Registered);
        if (active is null) return;
        WorkspaceResult result = await workspaceService.RefreshAsync(active.Id, cancellationToken);
        await ReloadWorkspaceContextAsync(
            result.Error ?? $"Refreshed {result.Workspace?.Name ?? active.Name} source context.",
            cancellationToken);
    }

    internal async ValueTask InspectWorkspaceAsync(CancellationToken cancellationToken)
    {
        string path = Current.Workspaces.RepositoryPath.Trim();
        if (path.Length == 0)
        {
            Publish(Current with
            {
                Workspaces = Current.Workspaces with { Status = "Enter a repository path." },
            });
            return;
        }

        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.InspectAsync(path, cancellationToken);
            Publish(Current with
            {
                Workspaces = Current.Workspaces with
                {
                    EntryPoints = result.EntryPoints,
                    Status = result.Error ?? $"Found {result.EntryPoints.Count} tracked .NET entry point(s).",
                },
            });
        }, "Workspace inspection");
    }

    internal async ValueTask RegisterWorkspaceAsync(
        string entryPoint,
        CancellationToken cancellationToken)
    {
        string path = Current.Workspaces.RepositoryPath.Trim();
        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.RegisterAsync(
                path,
                entryPoint,
                cancellationToken);
            if (result.Workspace is null)
            {
                Publish(Current with
                {
                    Workspaces = Current.Workspaces with
                    {
                        EntryPoints = result.EntryPoints,
                        Status = result.Error ?? "Workspace registration failed.",
                    },
                });
                return;
            }

            await ReloadWorkspaceContextAsync(
                $"Registered and selected {result.Workspace.Name}. Trust it before running tools.",
                cancellationToken);
        }, "Workspace registration");
    }

    internal async ValueTask SelectWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceView selected = await workspaceService.SelectAsync(
                workspaceId,
                cancellationToken);
            await ReloadWorkspaceContextAsync($"Selected {selected.Name}.", cancellationToken);
        }, "Workspace selection");

    internal async ValueTask SetWorkspaceTrustAsync(
        string workspaceId,
        bool isTrusted,
        CancellationToken cancellationToken) =>
        await RunWorkspaceCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.SetTrustAsync(
                workspaceId,
                isTrusted,
                cancellationToken);
            string name = result.Workspace?.Name ?? "workspace";
            await ReloadWorkspaceContextAsync(
                isTrusted ? $"Trusted {name}." : $"Revoked trust for {name}.",
                cancellationToken);
        }, isTrusted ? "Workspace trust" : "Workspace trust revocation");

    internal async ValueTask RefreshFrameworkAsync(CancellationToken cancellationToken) =>
        await RunFrameworkCommandAsync(async workspace =>
        {
            FrameworkSnapshot snapshot = await frameworkService.GetEffectiveAsync(
                workspace.Id,
                workspace.RootPath,
                cancellationToken);
            Publish(Current with
            {
                Framework = Current.Framework with
                {
                    Snapshot = snapshot,
                    Status = snapshot.IsValid
                        ? "Effective framework loaded."
                        : "Framework issues require attention.",
                },
            });
        }, "Framework inspection");

    internal async ValueTask SetPrivateFrameworkOverlayAsync(
        string? content,
        CancellationToken cancellationToken) =>
        await RunFrameworkCommandAsync(async workspace =>
        {
            FrameworkSnapshot snapshot = await frameworkService.SetPrivateOverlayAsync(
                workspace.Id,
                workspace.RootPath,
                content,
                cancellationToken);
            Publish(Current with
            {
                Framework = Current.Framework with
                {
                    Snapshot = snapshot,
                    Status = string.IsNullOrWhiteSpace(content)
                        ? "Private workspace overlay removed."
                        : "Private workspace overlay updated.",
                },
            });
        }, "Private framework overlay update");

}
