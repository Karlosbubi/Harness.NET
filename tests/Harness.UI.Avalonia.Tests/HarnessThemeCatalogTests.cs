using Harness.UI.Avalonia;

namespace Harness.UI.Avalonia.Tests;

public sealed class HarnessThemeCatalogTests
{
    [Fact]
    public void Built_in_concrete_themes_define_every_semantic_color()
    {
        int tokenCount = Enum.GetValues<UiThemeColorToken>().Length;

        foreach (UiThemeDefinition theme in HarnessThemeCatalog.BuiltIns
                     .Where(theme => theme.BaseVariant is not UiThemeBaseVariant.System))
        {
            Assert.Equal(tokenCount, theme.Colors.Count);
        }
    }

    [Fact]
    public void Toolkit_controls_are_public_and_business_neutral()
    {
        Assert.True(typeof(AdaptiveWorkspace).IsPublic);
        Assert.True(typeof(AccessibleSplitter).IsPublic);
        Assert.True(typeof(AccessibleIconButton).IsPublic);
        Assert.True(typeof(StatusIndicator).IsPublic);
        Assert.DoesNotContain(
            typeof(AdaptiveWorkspace).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "Harness.BusinessLogic" or "Harness.DataAccess");
    }
}
