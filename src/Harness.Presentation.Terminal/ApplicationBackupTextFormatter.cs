using Harness.BusinessLogic.Operations;

namespace Harness.Presentation.Terminal;

internal static class ApplicationBackupTextFormatter
{
    internal static string Format(ApplicationBackupView backup) => string.Join(
        "\n",
        $"Archive: {backup.Archive.Value}",
        $"Created: {backup.CreatedAt:O}",
        $"Schema: {backup.SchemaVersion.Value}",
        $"Database bytes: {backup.DatabaseBytes.Value}",
        $"Database SHA-256: {backup.DatabaseSha256.Value}",
        backup.WorkbenchLayoutSha256 is null
            ? "Workbench layout: not present"
            : $"Workbench layout: {backup.WorkbenchLayoutBytes?.Value} bytes · " +
              $"SHA-256 {backup.WorkbenchLayoutSha256.Value}",
        $"Archive SHA-256: {backup.ArchiveSha256.Value}",
        string.Empty,
        "Contents: consistent harness.db snapshot plus manifest.json",
        "Excluded: credentials, logs, caches, worktrees, and user repositories");
}
