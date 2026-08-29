namespace Harness.Architecture.Tests;

public sealed class SourceSizeBudgetTests
{
    private const int DefaultMaximumLines = 800;

    private static readonly IReadOnlyDictionary<string, int> BurnDownAllowlist =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/Harness.BusinessLogic/Agents/AgentRoleRunner.cs"] = 889,
            ["src/Harness.BusinessLogic/CodeIntelligence/WorkbenchCodeIntelligenceInteractions.cs"] = 920,
            ["src/Harness.BusinessLogic/Inspection/DeveloperGitService.cs"] = 1_244,
            ["src/Harness.BusinessLogic/Mcp/InboundMcpApplicationService.cs"] = 1_083,
            ["src/Harness.BusinessLogic/Workflows/GoalWorkflowService.cs"] = 1_337,
            ["src/Harness.DataAccess/CodeIntelligence/RoslynCodeIntelligenceEngine.cs"] = 1_896,
            ["src/Harness.DataAccess/CodeIntelligence/RoslynCodeIntelligencePresentation.cs"] = 853,
            ["src/Harness.DataAccess/Inspection/LibGitDeveloperGitRepository.cs"] = 1_962,
            ["src/Harness.DataAccess/Models/OpenRouter/OpenRouterModelProvider.cs"] = 861,
            ["src/Harness.DataAccess/Persistence/SqliteApplicationRestore.cs"] = 891,
            ["src/Harness.Presentation.Avalonia/AvaloniaPresentationStore.cs"] = 2_606,
            ["src/Harness.Presentation.Avalonia/GoalDialog.cs"] = 2_275,
            ["src/Harness.Presentation.Avalonia/MainWindow.cs"] = 1_599,
            ["src/Harness.Presentation.Avalonia/SettingsWindow.cs"] = 2_335,
            ["src/Harness.Presentation.Terminal/GoalDialog.cs"] = 1_262,
            ["src/Harness.Presentation.Terminal/HarnessWindow.cs"] = 834,
            ["tests/Harness.BusinessLogic.Tests/Agents/AgentRoleRunnerTests.cs"] = 838,
            ["tests/Harness.BusinessLogic.Tests/CodeIntelligence/WorkbenchCodeIntelligenceServiceTests.cs"] = 944,
            ["tests/Harness.BusinessLogic.Tests/Mutations/WorkspaceMutationServiceTests.cs"] = 1_217,
            ["tests/Harness.BusinessLogic.Tests/Workflows/GoalWorkflowServiceTests.cs"] = 832,
            ["tests/Harness.DataAccess.Tests/CodeIntelligence/RoslynCodeIntelligenceEngineTests.cs"] = 2_019,
            ["tests/Harness.DataAccess.Tests/Inspection/LibGitDeveloperGitRepositoryTests.cs"] = 1_185,
            ["tests/Harness.Presentation.Avalonia.Tests/AvaloniaPresentationStoreTests.cs"] = 1_720,
            ["tests/Harness.Presentation.Avalonia.Tests/PresentationControlTests.cs"] = 4_701,
        };

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
