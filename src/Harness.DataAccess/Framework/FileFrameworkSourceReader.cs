using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Framework;

internal sealed class FileFrameworkSourceReader(IApplicationPaths applicationPaths)
    : IFrameworkSourceReader
{
    private const long MaximumDocumentBytes = 1024 * 1024;

    public async ValueTask<FrameworkSourceResult> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        string canonicalRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            return new([], [$"Workspace root does not exist: {canonicalRoot}"]);
        }

        List<FrameworkDocument> documents = [];
        List<string> errors = [];
        await ReadIfPresentAsync(
            Path.Combine(applicationPaths.Current.ConfigDirectory, "framework.md"),
            "global",
            precedence: 0,
            isPrivate: true,
            documents,
            errors,
            cancellationToken);
        await ReadIfPresentAsync(
            Path.Combine(canonicalRoot, "AGENTS.md"),
            "repository",
            precedence: 1,
            isPrivate: false,
            documents,
            errors,
            cancellationToken);

        return new(documents, errors);
    }

    private static async ValueTask ReadIfPresentAsync(
        string path,
        string layer,
        int precedence,
        bool isPrivate,
        ICollection<FrameworkDocument> documents,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (!file.Exists)
        {
            return;
        }

        if (file.Length > MaximumDocumentBytes)
        {
            errors.Add($"Framework document exceeds 1 MiB: {file.FullName}");
            return;
        }

        try
        {
            string content = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(content))
            {
                documents.Add(new(
                    layer,
                    precedence,
                    file.FullName,
                    content,
                    isPrivate));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not read framework document '{file.FullName}': {exception.Message}");
        }
    }
}
