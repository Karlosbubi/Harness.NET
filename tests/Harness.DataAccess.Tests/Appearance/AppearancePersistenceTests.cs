using Harness.DataAccess.Appearance;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Persistence;

namespace Harness.DataAccess.Tests.Appearance;

public sealed class AppearancePersistenceTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(), "harness-appearance-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reads_bounded_declarative_theme()
    {
        StubApplicationPaths paths = new(CreatePaths());
        string themes = Path.Combine(paths.Current.ConfigDirectory, "themes");
        Directory.CreateDirectory(themes);
        await File.WriteAllTextAsync(Path.Combine(themes, "nord.xml"), """
            <harnessTheme version="1" id="nord" name="Nord" base="dark">
              <color token="Window" value="#2E3440" />
              <color token="TextPrimary" value="#ECEFF4" />
            </harnessTheme>
            """);

        UserThemeCatalog catalog = await new XdgUserThemeSource(paths).ReadAsync();

        UserThemeDefinition theme = Assert.Single(catalog.Themes);
        Assert.Empty(catalog.Issues);
        Assert.Equal("nord", theme.Id.Value);
        Assert.Equal("#2E3440", theme.Colors[ThemeColorToken.Window].Value);
    }

    [Fact]
    public async Task Rejects_dtd_and_unknown_tokens_without_stopping_catalog()
    {
        StubApplicationPaths paths = new(CreatePaths());
        string themes = Path.Combine(paths.Current.ConfigDirectory, "themes");
        Directory.CreateDirectory(themes);
        await File.WriteAllTextAsync(Path.Combine(themes, "hostile.xml"), """
            <!DOCTYPE theme [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <harnessTheme version="1" id="hostile" name="Hostile" base="dark" />
            """);
        await File.WriteAllTextAsync(Path.Combine(themes, "unknown.xml"), """
            <harnessTheme version="1" id="unknown" name="Unknown" base="light">
              <color token="ExecuteCode" value="#000000" />
            </harnessTheme>
            """);

        UserThemeCatalog catalog = await new XdgUserThemeSource(paths).ReadAsync();

        Assert.Empty(catalog.Themes);
        Assert.Equal(2, catalog.Issues.Count);
    }

    [Fact]
    public async Task Persists_selected_theme_in_migrated_database()
    {
        StubApplicationPaths paths = new(CreatePaths());
        await new SqliteDatabaseInitializer(paths).InitializeAsync();
        SqliteAppearancePreferenceStore store = new(paths);

        Assert.Equal("system", (await store.GetSelectedThemeAsync()).Value);
        await store.SaveSelectedThemeAsync(new("nord"));

        Assert.Equal("nord", (await store.GetSelectedThemeAsync()).Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(testDirectory, "config"),
        Path.Combine(testDirectory, "data"),
        Path.Combine(testDirectory, "state"),
        Path.Combine(testDirectory, "cache"),
        Path.Combine(testDirectory, "data", "harness.db"),
        Path.Combine(testDirectory, "state", "logs"),
        Path.Combine(testDirectory, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
