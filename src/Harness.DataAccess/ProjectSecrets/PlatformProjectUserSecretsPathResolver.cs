namespace Harness.DataAccess.ProjectSecrets;

internal sealed class PlatformProjectUserSecretsPathResolver : IProjectUserSecretsPathResolver
{
    public ProjectUserSecretsFilePath Resolve(string userSecretsId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSecretsId);
        string profile = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new InvalidOperationException("The current user profile directory is unavailable.");
        }

        string path = OperatingSystem.IsWindows()
            ? Path.Combine(profile, "Microsoft", "UserSecrets", userSecretsId, "secrets.json")
            : Path.Combine(profile, ".microsoft", "usersecrets", userSecretsId, "secrets.json");
        return new(Path.GetFullPath(path));
    }
}
