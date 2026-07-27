using Harness.BusinessLogic.Framework;
using Harness.Presentation.Terminal;

namespace Harness.Presentation.Terminal.Tests;

public sealed class FrameworkTextFormatterTests
{
    [Fact]
    public void Includes_lock_provenance_privacy_and_issues()
    {
        FrameworkSnapshot snapshot = new(
            [new("repository", 1, "/repo/AGENTS.md", "Use xUnit.", false)],
            [new("testing", "xunit", "repository", true, "harness.xml")],
            [new("locked_override", "Override blocked.", "testing", ["a", "b"])]);

        string text = FrameworkTextFormatter.Format(snapshot);

        Assert.Contains("[locked] testing = xunit", text, StringComparison.Ordinal);
        Assert.Contains("repository | harness.xml", text, StringComparison.Ordinal);
        Assert.Contains("[repository | shared] /repo/AGENTS.md", text, StringComparison.Ordinal);
        Assert.Contains("[locked_override] Override blocked.", text, StringComparison.Ordinal);
    }
}
