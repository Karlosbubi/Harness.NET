using Harness.DataAccess.Execution;

namespace Harness.DataAccess.Tests.Execution;

public sealed class TrxTestResultParserTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-trx-parser-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Parses_typed_case_outcomes_names_and_durations_without_output_payloads()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "results.trx"), """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="a" testName="Adds values" outcome="Passed" duration="00:00:00.125" />
                <UnitTestResult testId="b" testName="Skips values" outcome="NotExecuted" duration="00:00:00" />
              </Results>
              <TestDefinitions>
                <UnitTest id="a"><TestMethod className="Demo.CalculatorTests" name="Adds" /></UnitTest>
                <UnitTest id="b"><TestMethod className="Demo.CalculatorTests" name="Skips" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """);

        TrxTestResultParse result = TrxTestResultParser.ParseDirectory(root);

        Assert.False(result.IsTruncated);
        Assert.Collection(result.Cases,
            item =>
            {
                Assert.Equal("Demo.CalculatorTests.Adds", item.FullyQualifiedName.Value);
                Assert.Equal("Adds values", item.DisplayName.Value);
                Assert.Equal(DotNetTestOutcome.Passed, item.Outcome);
                Assert.Equal(125, item.DurationMilliseconds);
            },
            item => Assert.Equal(DotNetTestOutcome.Skipped, item.Outcome));
        Assert.DoesNotContain(result.Cases[0].GetType().GetProperties(), property =>
            property.Name.Contains("Output", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
