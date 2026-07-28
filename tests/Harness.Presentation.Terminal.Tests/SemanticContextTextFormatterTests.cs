using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Retrieval;

namespace Harness.Presentation.Terminal.Tests;

public sealed class SemanticContextTextFormatterTests
{
    [Fact]
    public void Formats_context_provenance_usage_cost_and_content()
    {
        SemanticSearchResult result = new(
            Partition: null,
            [new("src/Feature.cs", 10, 20, "relevant content", new(0.125))],
            new(42, new MicroUsdAmount(321)),
            ErrorCode: null,
            Error: null);

        string text = SemanticContextTextFormatter.Format(result);

        Assert.Contains("src/Feature.cs:10-20", text, StringComparison.Ordinal);
        Assert.Contains("relevant content", text, StringComparison.Ordinal);
        Assert.Contains("42", text, StringComparison.Ordinal);
        Assert.Contains("$0.000321", text, StringComparison.Ordinal);
    }
}
