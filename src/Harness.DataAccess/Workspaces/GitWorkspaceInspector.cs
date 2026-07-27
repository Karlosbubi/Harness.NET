using LibGit2Sharp;

namespace Harness.DataAccess.Workspaces;

internal sealed class GitWorkspaceInspector : IWorkspaceInspector
{
    public ValueTask<WorkspaceInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ValueTask.FromResult(Failure(path, exception.Message));
        }

        string? repositoryPath = Repository.Discover(fullPath);
        if (repositoryPath is null)
        {
            return ValueTask.FromResult(Failure(fullPath, "No Git repository was found."));
        }

        try
        {
            using Repository repository = new(repositoryPath);
            string root = Path.GetFullPath(repository.Info.WorkingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar);
            string[] entryPoints = repository.Index
                .Select(entry => entry.Path)
                .Where(IsDotNetEntryPoint)
                .Select(relative => Path.GetFullPath(Path.Combine(root, relative)))
                .Order(StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(new WorkspaceInspection(
                root,
                Path.GetFileName(root),
                repository.Head.FriendlyName,
                repository.RetrieveStatus().IsDirty,
                entryPoints,
                Error: null));
        }
        catch (LibGit2SharpException exception)
        {
            return ValueTask.FromResult(Failure(fullPath, exception.Message));
        }
    }

    private static bool IsDotNetEntryPoint(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static WorkspaceInspection Failure(string path, string error) =>
        new(path, Path.GetFileName(path), string.Empty, false, [], error);
}
