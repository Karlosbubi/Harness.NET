using Harness.BusinessLogic.Operations;

namespace Harness.Presentation.Terminal.Tests;

public sealed class ApplicationRestoreTextFormatterTests
{
    [Fact]
    public void Shows_verified_restore_evidence_and_absent_layout_effect()
    {
        ApplicationRestoreView restore = new(
            new("/backups/state.zip"), new(new string('a', 64)),
            new(new string('b', 64)), new(1024), null, null, new(21),
            DateTimeOffset.Parse("2026-07-31T12:00:00Z"),
            RestoreArchiveFormat.Version2);

        string text = ApplicationRestoreTextFormatter.Format(restore);

        Assert.Contains("/backups/state.zip", text, StringComparison.Ordinal);
        Assert.Contains(new string('a', 64), text, StringComparison.Ordinal);
        Assert.Contains("current layout will be removed", text, StringComparison.Ordinal);
        Assert.Contains("Schema version: 21", text, StringComparison.Ordinal);
    }
}
