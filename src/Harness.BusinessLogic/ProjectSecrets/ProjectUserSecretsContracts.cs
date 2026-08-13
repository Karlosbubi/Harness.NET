using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.ProjectSecrets;

public sealed record ProjectUserSecretsProjectPath(string Value);
public sealed record ProjectUserSecretKey(string Value);

public sealed record ProjectUserSecretValue(string Value)
{
    public override string ToString() => "[REDACTED]";
}

public enum ProjectUserSecretsProjectState
{
    Available,
    UserSecretsIdMissing,
    UserSecretsIdUnsupported,
    ProjectMissing,
    ProjectInvalid,
    StoreInvalid,
}

public sealed record ProjectUserSecretsProjectView(
    ProjectUserSecretsProjectPath Path,
    ProjectUserSecretsProjectState State,
    int SecretCount,
    string Status);

public sealed record ProjectUserSecretsProjectListResult(
    IReadOnlyList<ProjectUserSecretsProjectView> Projects,
    string? ErrorCode,
    string? Error);

public sealed record ProjectUserSecretListResult(
    ProjectUserSecretsProjectView? Project,
    IReadOnlyList<ProjectUserSecretKey> Keys,
    string? ErrorCode,
    string? Error);

public enum ProjectUserSecretValueOutcome
{
    Succeeded,
    NotFound,
    Unavailable,
    DisclosureBlocked,
}

public sealed record ProjectUserSecretDisclosure(
    ProjectUserSecretValue Value,
    ISensitiveDisplayLease Lease) : IDisposable
{
    public void Dispose() => Lease.Dispose();

    public override string ToString() =>
        $"{nameof(ProjectUserSecretDisclosure)} {{ Value = [REDACTED], Lease = active }}";
}

public sealed record ProjectUserSecretRevealResult(
    ProjectUserSecretValueOutcome Outcome,
    ProjectUserSecretDisclosure? Disclosure,
    string? ErrorCode,
    string? Error)
{
    public override string ToString() =>
        $"{nameof(ProjectUserSecretRevealResult)} {{ Outcome = {Outcome}, Disclosure = [REDACTED], ErrorCode = {ErrorCode}, Error = {Error} }}";
}

public sealed record ProjectUserSecretCopyResult(
    ProjectUserSecretValueOutcome Outcome,
    ProjectUserSecretValue? Value,
    string? ErrorCode,
    string? Error)
{
    public override string ToString() =>
        $"{nameof(ProjectUserSecretCopyResult)} {{ Outcome = {Outcome}, Value = [REDACTED], ErrorCode = {ErrorCode}, Error = {Error} }}";
}

public enum ProjectUserSecretMutationOutcome
{
    Succeeded,
    AlreadyExists,
    NotFound,
    Unavailable,
    Conflict,
}

public sealed record ProjectUserSecretMutationResult(
    ProjectUserSecretMutationOutcome Outcome,
    ProjectUserSecretsProjectView? Project,
    string? ErrorCode,
    string? Error);

public interface IProjectUserSecretsService
{
    ValueTask<ProjectUserSecretsProjectListResult> ListProjectsAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectUserSecretListResult> ListAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectUserSecretRevealResult> RevealAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectUserSecretCopyResult> CopyAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectUserSecretMutationResult> AddAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        ProjectUserSecretValue value,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectUserSecretMutationResult> ChangeAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        ProjectUserSecretValue value,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectUserSecretMutationResult> DeleteAsync(
        WorkspaceId workspaceId,
        ProjectUserSecretsProjectPath projectPath,
        ProjectUserSecretKey key,
        CancellationToken cancellationToken = default);
}
