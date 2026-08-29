namespace Harness.Architecture.Tests;

public sealed class SourceSizeBudgetTests
{
    private const int DefaultMaximumLines = 800;

    private static readonly IReadOnlyDictionary<string, int> BurnDownAllowlist =
        new Dictionary<string, int>(StringComparer.Ordinal);

    [Fact]
    public void Production_and_test_sources_respect_the_shrink_only_line_budget()
    {
        string solutionRoot = FindSolutionRoot();
        string[] roots = ["src", "tests"];
        Dictionary<string, int> measured = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(solutionRoot, root), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOutput(path))
            .ToDictionary(
                path => Normalize(Path.GetRelativePath(solutionRoot, path)),
                path => File.ReadLines(path).Count(),
                StringComparer.Ordinal);

        foreach ((string path, int lines) in measured)
        {
            int maximum = BurnDownAllowlist.GetValueOrDefault(path, DefaultMaximumLines);
            Assert.True(
                lines <= maximum,
                $"{path} has {lines:N0} lines; its current budget is {maximum:N0}.");
        }

        foreach ((string path, int maximum) in BurnDownAllowlist)
        {
            Assert.True(measured.TryGetValue(path, out int lines), $"Allowlisted file is missing: {path}");
            Assert.True(
                lines > DefaultMaximumLines,
                $"{path} is now within {DefaultMaximumLines:N0} lines; remove it from the burn-down allowlist.");
            Assert.True(maximum > DefaultMaximumLines, $"Invalid allowlist budget for {path}.");
        }
    }

    [Fact]
    public void Host_program_remains_a_thin_composition_entry_point()
    {
        string path = Path.Combine(FindSolutionRoot(), "src", "Harness.Host", "Program.cs");

        int lines = File.ReadLines(path).Count();

        Assert.True(lines <= 200, $"src/Harness.Host/Program.cs has {lines:N0} lines; the limit is 200.");
    }

    private static bool IsGeneratedOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Normalize(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/');

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Harness.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate Harness.slnx from the test output directory.");
    }
}
