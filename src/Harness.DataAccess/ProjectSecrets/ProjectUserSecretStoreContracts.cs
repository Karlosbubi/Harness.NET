namespace Harness.DataAccess.ProjectSecrets;

public sealed record StoredProjectUserSecretsRequest(
    string WorkspaceRoot,
    string ProjectPath);

public sealed record StoredProjectUserSecretKey(string Value);

public sealed record StoredProjectUserSecretValue(string Value)
{
    public override string ToString() => "[REDACTED]";
}

public enum StoredProjectUserSecretsState
{
    Available,
    UserSecretsIdMissing,
    UserSecretsIdUnsupported,
    ProjectMissing,
    ProjectInvalid,
    StoreInvalid,
}

public sealed record StoredProjectUserSecretsDescriptor(
    string ProjectPath,
    StoredProjectUserSecretsState State,
    int SecretCount,
    string? ErrorCode,
    string? Error);

public sealed record StoredProjectUserSecretList(
    StoredProjectUserSecretsDescriptor Project,
    IReadOnlyList<StoredProjectUserSecretKey> Keys);

public enum StoredProjectUserSecretReadState
{
    Succeeded,
    NotFound,
    Unavailable,
}

public sealed record StoredProjectUserSecretReadResult(
    StoredProjectUserSecretReadState State,
    StoredProjectUserSecretValue? Value,
    string? ErrorCode,
    string? Error)
{
    public override string ToString() =>
        $"{nameof(StoredProjectUserSecretReadResult)} {{ State = {State}, Value = [REDACTED], ErrorCode = {ErrorCode}, Error = {Error} }}";
}

public enum StoredProjectUserSecretMutationState
{
    Succeeded,
    AlreadyExists,
    NotFound,
    Unavailable,
    Conflict,
}

public sealed record StoredProjectUserSecretMutationResult(
    StoredProjectUserSecretMutationState State,
    StoredProjectUserSecretsDescriptor? Project,
    string? ErrorCode,
    string? Error);

public interface IProjectUserSecretStore
{
    ValueTask<StoredProjectUserSecretsDescriptor> DescribeAsync(
        StoredProjectUserSecretsRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<StoredProjectUserSecretList> ListAsync(
        StoredProjectUserSecretsRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<StoredProjectUserSecretReadResult> ReadAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        CancellationToken cancellationToken = default);

    ValueTask<StoredProjectUserSecretMutationResult> AddAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        StoredProjectUserSecretValue value,
        CancellationToken cancellationToken = default);

    ValueTask<StoredProjectUserSecretMutationResult> ChangeAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        StoredProjectUserSecretValue value,
        CancellationToken cancellationToken = default);

    ValueTask<StoredProjectUserSecretMutationResult> DeleteAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectUserSecretsFilePath(string Value);

public interface IProjectUserSecretsPathResolver
{
    ProjectUserSecretsFilePath Resolve(string userSecretsId);
}
