using Harness.BusinessLogic.Operations;

namespace Harness.Presentation.Terminal.Tests;

public sealed class ApplicationBackupTextFormatterTests
{
    [Fact]
    public void Formats_verification_and_exclusion_evidence()
    {
        ApplicationBackupView backup = new(
            new("/tmp/backup.zip"),
            new(new string('a', 64)),
            new(new string('b', 64)),
            new(1234),
            new(new string('c', 64)),
            new(456),
            new(17),
            DateTimeOffset.Parse("2026-07-29T12:00:00Z"));

        string text = ApplicationBackupTextFormatter.Format(backup);

        Assert.Contains("schema", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(new string('a', 64), text, StringComparison.Ordinal);
        Assert.Contains(new string('b', 64), text, StringComparison.Ordinal);
        Assert.Contains("credentials", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worktrees", text, StringComparison.OrdinalIgnoreCase);
    }
}
