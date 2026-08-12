using Harness.DataAccess.Secrets;

namespace Harness.Host;

internal static class EvaluationMcpTokenBootstrap
{
    internal const string TokenReference = "inbound-mcp-bearer-token";
    private const int TokenLength = 64;
    private const UnixFileMode AllowedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static async ValueTask SeedAsync(
        string evaluationRoot,
        string tokenFile,
        ISecretStore secrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenFile);
        ArgumentNullException.ThrowIfNull(secrets);
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("MCP evaluation token bootstrap currently requires Linux.");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evaluationRoot));
        string path = Path.GetFullPath(tokenFile);
        if (!Path.GetDirectoryName(path)!.Equals(root, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The MCP evaluation token file must be directly inside the evaluation root.",
                nameof(tokenFile));
        }

        FileInfo file = new(path);
        if (!file.Exists || file.LinkTarget is not null ||
            (File.GetUnixFileMode(path) & ~AllowedMode) != 0)
        {
            throw new ArgumentException(
                "The MCP evaluation token file must be a regular owner-only file.",
                nameof(tokenFile));
        }

        string token;
        try
        {
            token = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        finally
        {
            File.Delete(path);
        }

        if (token.Length != TokenLength)
        {
            throw new ArgumentException(
                "The MCP evaluation token must be a 48-byte Base64 value.",
                nameof(tokenFile));
        }

        try
        {
            if (Convert.FromBase64String(token).Length != 48)
                throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The MCP evaluation token must be a 48-byte Base64 value.",
                nameof(tokenFile), exception);
        }

        await secrets.SetAsync(new(TokenReference), token, cancellationToken);
    }
}
