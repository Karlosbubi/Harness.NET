using Harness.DataAccess.CodeIntelligence;

namespace Harness.DataAccess.Tests.CodeIntelligence;

public sealed partial class RoslynCodeIntelligenceEngineTests
{
    [Fact]
    public async Task Discovers_framework_tests_traits_source_and_bounded_search_with_Roslyn()
    {
        const string source = """
            namespace Xunit
            {
                class FactAttribute : System.Attribute { public string? DisplayName { get; set; } }
                class TheoryAttribute : FactAttribute { }
                class TraitAttribute(string name, string value) : System.Attribute { }
            }
            namespace NUnit.Framework
            {
                class TestAttribute : System.Attribute { }
                class TestCaseAttribute(params object[] values) : System.Attribute { }
                class CategoryAttribute(string name) : System.Attribute { }
            }
            namespace Microsoft.VisualStudio.TestTools.UnitTesting
            {
                class TestMethodAttribute : System.Attribute { }
            }
            namespace Demo
            {
                class CustomFactAttribute : Xunit.FactAttribute { }
                class CalculatorTests
                {
                    [CustomFact(DisplayName = "adds values")]
                    [Xunit.Trait("Category", "Fast")]
                    public void Adds() { }

                    [Xunit.Theory]
                    public void Adds_many(int value) { }

                    [NUnit.Framework.Test]
                    [NUnit.Framework.TestCase(1)]
                    [NUnit.Framework.Category("Integration")]
                    public void NUnit_case(int value) { }

                    [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
                    public void MsTest_case() { }
                }
            }
            """;
        await CreateProjectAsync(source);
        using RoslynCodeIntelligenceEngine engine = CreateEngine();
        CodeIntelligenceContextId contextId = new("test-discovery-context");
        CodeIntelligenceSessionResult session = await engine.OpenAsync(OpenRequest(contextId));

        CodeIntelligenceTestDiscoveryResult all = await engine.DiscoverTestsAsync(new(
            contextId, session.SessionId!, Query: null, MaximumResults: 20, Offset: 0));
        CodeIntelligenceTestDiscoveryResult filtered = await engine.DiscoverTestsAsync(new(
            contextId, session.SessionId!, "Fast", MaximumResults: 20, Offset: 0));
        CodeIntelligenceTestDiscoveryResult page = await engine.DiscoverTestsAsync(new(
            contextId, session.SessionId!, Query: null, MaximumResults: 2, Offset: 0));

        Assert.Equal(CodeIntelligenceResultState.Ready, all.State);
        Assert.Equal(4, all.Tests.Count);
        Assert.Equal(4, all.Tests.Select(item => item.Id).Distinct().Count());
        Assert.Contains(all.Tests, item =>
            item.Framework is CodeIntelligenceTestFramework.XUnit &&
            item.DisplayName.Value == "adds values" &&
            !item.IsParameterized &&
            item.Traits.Any(trait => trait.Name.Value == "Category" &&
                trait.Value.Value == "Fast"));
        Assert.Contains(all.Tests, item =>
            item.Framework is CodeIntelligenceTestFramework.XUnit && item.IsParameterized);
        Assert.Contains(all.Tests, item =>
            item.Framework is CodeIntelligenceTestFramework.NUnit && item.IsParameterized);
        Assert.Contains(all.Tests, item =>
            item.Framework is CodeIntelligenceTestFramework.MSTest && !item.IsParameterized);
        Assert.All(all.Tests, item =>
        {
            Assert.Equal("Sample.csproj", item.ProjectPath.Value);
            Assert.Equal("Sample.cs", item.Path.Value);
            Assert.True(item.Range.Start.Line >= 0);
        });
        Assert.Equal("Demo.CalculatorTests.Adds", Assert.Single(filtered.Tests)
            .FullyQualifiedName.Value);
        Assert.Equal(2, page.Tests.Count);
        Assert.True(page.IsTruncated);
        Assert.Equal(2, page.Continuation);
    }
}
