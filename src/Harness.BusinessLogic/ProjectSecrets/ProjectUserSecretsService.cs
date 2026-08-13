using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.ProjectSecrets;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.ProjectSecrets;

internal sealed class ProjectUserSecretsService(
    IWorkspaceStore workspaceStore,
    IWorkspaceDotNetInspector dotNetInspector,
    IProjectUserSecretStore secretStore,
    ISensitiveDisplayGuard sensitiveDisplayGuard) : IProjectUserSecretsService
{
    private const int MaximumKeyLength = 1024;
    private const int MaximumValueLength = 1024 * 1024;

    public async ValueTask<ProjectUserSecretsProjectListResult> ListProjectsAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        WorkspaceResolution resolution = await ResolveWorkspaceAsync(workspaceId, cancellationToken);
        if (resolution.Workspace is null || resolution.DotNetInfo is null)
        {
            return new([], resolution.ErrorCode, resolution.Error);
        }

        List<ProjectUserSecretsProjectView> projects = [];
        foreach (DotNetProjectInfo project in resolution.DotNetInfo.Projects
                     .OrderBy(project => project.Path, StringComparer.OrdinalIgnoreCase))
        {
            StoredProjectUserSecretsDescriptor descriptor = await secretStore.DescribeAsync(
                new(resolution.Workspace.RootPath, project.Path), cancellationToken);
            projects.Add(Map(descriptor));
        }
        return new(projects, null, null);
    }

    public async ValueTask<ProjectUserSecretListResult> ListAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        CancellationToken cancellationToken = default)
    {
        ProjectResolution resolution = await ResolveProjectAsync(
            workspaceId, projectPath, cancellationToken);
        if (resolution.Request is null)
        {
            return new(null, [], resolution.ErrorCode, resolution.Error);
        }

        StoredProjectUserSecretList list = await secretStore.ListAsync(
            resolution.Request, cancellationToken);
        ProjectUserSecretsProjectView project = Map(list.Project);
        return list.Project.State is StoredProjectUserSecretsState.Available
            ? new(project, list.Keys.Select(key => new ProjectUserSecretKey(key.Value)).ToArray(),
                null, null)
            : new(project, [], list.Project.ErrorCode, list.Project.Error);
    }

    public async ValueTask<ProjectUserSecretRevealResult> RevealAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(key?.Value))
        {
            return RevealFailure(ProjectUserSecretValueOutcome.Unavailable,
                "invalid_secret_key", "A valid project secret key is required.");
        }
        if (!sensitiveDisplayGuard.TryBeginSensitiveDisplay(
                SensitiveDisplayKind.ProjectUserSecret, out ISensitiveDisplayLease? lease))
        {
            return RevealFailure(ProjectUserSecretValueOutcome.DisclosureBlocked,
                "sensitive_disclosure_blocked",
                "Wait for the active visual capture or hide the other sensitive value first.");
        }
        ProjectUserSecretKey validKey = key!;
        ISensitiveDisplayLease disclosureLease = lease!;

        try
        {
            ProjectUserSecretCopyResult read = await ReadAsync(
                workspaceId, projectPath, validKey, cancellationToken);
            if (read.Outcome is not ProjectUserSecretValueOutcome.Succeeded || read.Value is null)
            {
                disclosureLease.Dispose();
                return new(read.Outcome, null, read.ErrorCode, read.Error);
            }
            return new(ProjectUserSecretValueOutcome.Succeeded,
                new(read.Value, disclosureLease), null, null);
        }
        catch
        {
            disclosureLease.Dispose();
            throw;
        }
    }

    public ValueTask<ProjectUserSecretCopyResult> CopyAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken = default)
    {
        return IsValidKey(key?.Value)
            ? ReadAsync(workspaceId, projectPath, key!, cancellationToken)
            : ValueTask.FromResult(CopyFailure(ProjectUserSecretValueOutcome.Unavailable,
                "invalid_secret_key", "A valid project secret key is required."));
    }

    public ValueTask<ProjectUserSecretMutationResult> AddAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        ProjectUserSecretValue value,
        CancellationToken cancellationToken = default) =>
        MutateAsync(workspaceId, projectPath, key, value, MutationKind.Add, cancellationToken);

    public ValueTask<ProjectUserSecretMutationResult> ChangeAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        ProjectUserSecretValue value,
        CancellationToken cancellationToken = default) =>
        MutateAsync(workspaceId, projectPath, key, value, MutationKind.Change, cancellationToken);

    public ValueTask<ProjectUserSecretMutationResult> DeleteAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken = default) =>
        MutateAsync(workspaceId, projectPath, key, value: null, MutationKind.Delete,
            cancellationToken);

    private async ValueTask<ProjectUserSecretCopyResult> ReadAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken)
    {
        ProjectResolution resolution = await ResolveProjectAsync(
            workspaceId, projectPath, cancellationToken);
        if (resolution.Request is null)
        {
            return CopyFailure(ProjectUserSecretValueOutcome.Unavailable,
                resolution.ErrorCode!, resolution.Error!);
        }

        StoredProjectUserSecretReadResult read = await secretStore.ReadAsync(
            resolution.Request, new(key.Value), cancellationToken);
        return read.State switch
        {
            StoredProjectUserSecretReadState.Succeeded when read.Value is not null =>
                new(ProjectUserSecretValueOutcome.Succeeded,
                    new(read.Value.Value), null, null),
            StoredProjectUserSecretReadState.NotFound =>
                CopyFailure(ProjectUserSecretValueOutcome.NotFound,
                    read.ErrorCode ?? "secret_not_found",
                    read.Error ?? "The selected project secret no longer exists."),
            _ => CopyFailure(ProjectUserSecretValueOutcome.Unavailable,
                read.ErrorCode ?? "project_user_secrets_unavailable",
                read.Error ?? "Project User Secrets are unavailable."),
        };
    }

    private async ValueTask<ProjectUserSecretMutationResult> MutateAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        ProjectUserSecretValue? value,
        MutationKind kind,
        CancellationToken cancellationToken)
    {
        if (!IsValidKey(key?.Value))
        {
            return MutationFailure("invalid_secret_key", "A valid project secret key is required.");
        }
        if (kind is not MutationKind.Delete &&
            (value is null || value.Value.Length > MaximumValueLength))
        {
            return MutationFailure("invalid_secret_value",
                $"A project secret value of at most {MaximumValueLength} characters is required.");
        }
        string keyValue = key!.Value;
        string? secretValue = value?.Value;

        ProjectResolution resolution = await ResolveProjectAsync(
            workspaceId, projectPath, cancellationToken);
        if (resolution.Request is null)
        {
            return MutationFailure(resolution.ErrorCode!, resolution.Error!);
        }

        StoredProjectUserSecretMutationResult result = kind switch
        {
            MutationKind.Add => await secretStore.AddAsync(resolution.Request,
                new(keyValue), new(secretValue!), cancellationToken),
            MutationKind.Change => await secretStore.ChangeAsync(resolution.Request,
                new(keyValue), new(secretValue!), cancellationToken),
            MutationKind.Delete => await secretStore.DeleteAsync(resolution.Request,
                new(keyValue), cancellationToken),
            _ => throw new InvalidOperationException("Unsupported project secret mutation."),
        };
        return new(Map(result.State), result.Project is null ? null : Map(result.Project),
            result.ErrorCode, result.Error);
    }

    private async ValueTask<WorkspaceResolution> ResolveWorkspaceAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        if (workspaceId is null || string.IsNullOrWhiteSpace(workspaceId.Value))
        {
            return WorkspaceFailure("invalid_workspace", "An active workspace is required.");
        }
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (workspace is null || !workspace.Id.Equals(workspaceId.Value, StringComparison.Ordinal))
        {
            return WorkspaceFailure("workspace_not_active", "The requested workspace is not active.");
        }
        if (!workspace.IsTrusted)
        {
            return WorkspaceFailure("workspace_not_trusted",
                "Trust the workspace before managing project User Secrets.");
        }

        WorkspaceDotNetInfo info = await dotNetInspector.InspectAsync(
            workspace.RootPath, workspace.EntryPoint, cancellationToken);
        return info.ErrorCode is null
            ? new(workspace, info, null, null)
            : WorkspaceFailure(info.ErrorCode, info.Error ?? ".NET project inspection failed.");
    }

    private async ValueTask<ProjectResolution> ResolveProjectAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        CancellationToken cancellationToken)
    {
        if (projectPath is null || string.IsNullOrWhiteSpace(projectPath.Value))
        {
            return ProjectFailure("invalid_project", "A project is required.");
        }
        WorkspaceResolution workspace = await ResolveWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace.Workspace is null || workspace.DotNetInfo is null)
        {
            return ProjectFailure(workspace.ErrorCode!, workspace.Error!);
        }
        DotNetProjectInfo? project = workspace.DotNetInfo.Projects.FirstOrDefault(item =>
            item.Path.Equals(projectPath.Value, StringComparison.Ordinal));
        return project is null
            ? ProjectFailure("project_not_in_workspace",
                "Select a project from the active inspected workspace.")
            : new(new(workspace.Workspace.RootPath, project.Path), null, null);
    }

    private static ProjectUserSecretsProjectView Map(
        StoredProjectUserSecretsDescriptor descriptor) => new(
        new(descriptor.ProjectPath),
        descriptor.State switch
        {
            StoredProjectUserSecretsState.Available => ProjectUserSecretsProjectState.Available,
            StoredProjectUserSecretsState.UserSecretsIdMissing =>
                ProjectUserSecretsProjectState.UserSecretsIdMissing,
            StoredProjectUserSecretsState.UserSecretsIdUnsupported =>
                ProjectUserSecretsProjectState.UserSecretsIdUnsupported,
            StoredProjectUserSecretsState.ProjectMissing =>
                ProjectUserSecretsProjectState.ProjectMissing,
            StoredProjectUserSecretsState.ProjectInvalid =>
                ProjectUserSecretsProjectState.ProjectInvalid,
            StoredProjectUserSecretsState.StoreInvalid =>
                ProjectUserSecretsProjectState.StoreInvalid,
            _ => throw new InvalidOperationException("Unsupported project User Secrets state."),
        },
        descriptor.SecretCount,
        descriptor.Error ?? descriptor.State switch
        {
            StoredProjectUserSecretsState.Available when descriptor.SecretCount == 1 => "1 secret",
            StoredProjectUserSecretsState.Available => $"{descriptor.SecretCount} secrets",
            _ => "Project User Secrets are unavailable.",
        });

    private static ProjectUserSecretMutationOutcome Map(
        StoredProjectUserSecretMutationState state) => state switch
    {
        StoredProjectUserSecretMutationState.Succeeded => ProjectUserSecretMutationOutcome.Succeeded,
        StoredProjectUserSecretMutationState.AlreadyExists => ProjectUserSecretMutationOutcome.AlreadyExists,
        StoredProjectUserSecretMutationState.NotFound => ProjectUserSecretMutationOutcome.NotFound,
        StoredProjectUserSecretMutationState.Conflict => ProjectUserSecretMutationOutcome.Conflict,
        StoredProjectUserSecretMutationState.Unavailable => ProjectUserSecretMutationOutcome.Unavailable,
        _ => throw new InvalidOperationException("Unsupported project secret mutation state."),
    };

    private static bool IsValidKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumKeyLength &&
        value.Equals(value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl) &&
        !value.StartsWith(':') && !value.EndsWith(':') &&
        !value.Contains("::", StringComparison.Ordinal);

    private static ProjectUserSecretRevealResult RevealFailure(
        ProjectUserSecretValueOutcome outcome,
        string code,
        string error) => new(outcome, null, code, error);

    private static ProjectUserSecretCopyResult CopyFailure(
        ProjectUserSecretValueOutcome outcome,
        string code,
        string error) => new(outcome, null, code, error);

    private static ProjectUserSecretMutationResult MutationFailure(string code, string error) =>
        new(ProjectUserSecretMutationOutcome.Unavailable, null, code, error);

    private static WorkspaceResolution WorkspaceFailure(string code, string error) =>
        new(null, null, code, error);

    private static ProjectResolution ProjectFailure(string code, string error) =>
        new(null, code, error);

    private sealed record WorkspaceResolution(
        RegisteredWorkspace? Workspace,
        WorkspaceDotNetInfo? DotNetInfo,
        string? ErrorCode,
        string? Error);

    private sealed record ProjectResolution(
        StoredProjectUserSecretsRequest? Request,
        string? ErrorCode,
        string? Error);

    private enum MutationKind
    {
        Add,
        Change,
        Delete,
    }
}
