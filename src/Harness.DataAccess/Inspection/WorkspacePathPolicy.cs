namespace Harness.DataAccess.Inspection;

internal static class WorkspacePathPolicy
{
    private static readonly char[] Separators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    internal static bool TryResolve(
        string workspaceRoot,
        string relativePath,
        out string canonicalRoot,
        out string confinedPath,
        out string targetPath,
        out string? errorCode,
        out string? error)
    {
        canonicalRoot = string.Empty;
        confinedPath = relativePath;
        targetPath = string.Empty;
        errorCode = null;
        error = null;
        if (string.IsNullOrWhiteSpace(workspaceRoot) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            errorCode = "invalid_path";
            error = "A workspace root and relative path are required.";
            return false;
        }

        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            targetPath = Path.GetFullPath(relativePath, canonicalRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            errorCode = "invalid_path";
            error = exception.Message;
            return false;
        }

        if (!Directory.Exists(canonicalRoot))
        {
            errorCode = "workspace_missing";
            error = "The workspace root does not exist.";
            return false;
        }

        confinedPath = Path.GetRelativePath(canonicalRoot, targetPath);
        if (Path.IsPathRooted(relativePath) || IsOutsideWorkspace(confinedPath))
        {
            errorCode = "outside_workspace";
            error = "The path must remain inside the workspace.";
            return false;
        }

        if (ContainsReparsePoint(canonicalRoot, confinedPath))
        {
            errorCode = "symlink_not_allowed";
            error = "Symbolic links are not allowed in inspected paths.";
            return false;
        }

        return true;
    }

    private static bool IsOutsideWorkspace(string relativePath) =>
        relativePath.Equals("..", StringComparison.Ordinal) ||
        relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
        relativePath.Equals(".", StringComparison.Ordinal);

    private static bool ContainsReparsePoint(string root, string relativePath)
    {
        string current = root;
        foreach (string segment in relativePath.Split(
                     Separators,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
