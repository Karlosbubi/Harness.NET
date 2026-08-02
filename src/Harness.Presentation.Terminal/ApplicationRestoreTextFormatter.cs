using Harness.BusinessLogic.Operations;

namespace Harness.Presentation.Terminal;

internal static class ApplicationRestoreTextFormatter
{
    internal static string Format(ApplicationRestoreView restore) => string.Join('\n',
        $"Archive: {restore.Archive.Value}",
        $"Archive SHA-256: {restore.ArchiveSha256.Value}",
        $"Database SHA-256: {restore.DatabaseSha256.Value}",
        $"Database bytes: {restore.DatabaseBytes.Value}",
        restore.WorkbenchLayoutSha256 is null
            ? "Workbench layout: not present (current layout will be removed)"
            : $"Workbench layout: {restore.WorkbenchLayoutBytes?.Value} bytes · " +
              $"SHA-256 {restore.WorkbenchLayoutSha256.Value}",
        $"Schema version: {restore.SchemaVersion.Value}",
        $"Created: {restore.CreatedAt:O}");
}
