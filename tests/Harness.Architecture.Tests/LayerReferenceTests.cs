using System.Xml.Linq;

namespace Harness.Architecture.Tests;

public sealed class LayerReferenceTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["src/Harness.DataAccess/Harness.DataAccess.csproj"] = [],
            ["src/Harness.BusinessLogic/Harness.BusinessLogic.csproj"] =
            [
                "src/Harness.DataAccess/Harness.DataAccess.csproj",
            ],
            ["src/Harness.UI.Avalonia/Harness.UI.Avalonia.csproj"] = [],
            ["src/Harness.Presentation.Avalonia/Harness.Presentation.Avalonia.csproj"] =
            [
                "src/Harness.BusinessLogic/Harness.BusinessLogic.csproj",
                "src/Harness.UI.Avalonia/Harness.UI.Avalonia.csproj",
            ],
            ["src/Harness.Presentation.Terminal/Harness.Presentation.Terminal.csproj"] =
            [
                "src/Harness.BusinessLogic/Harness.BusinessLogic.csproj",
            ],
            ["src/Harness.Host/Harness.Host.csproj"] =
            [
                "src/Harness.BusinessLogic/Harness.BusinessLogic.csproj",
                "src/Harness.DataAccess/Harness.DataAccess.csproj",
                "src/Harness.Presentation.Avalonia/Harness.Presentation.Avalonia.csproj",
                "src/Harness.Presentation.Terminal/Harness.Presentation.Terminal.csproj",
            ],
        };

    [Fact]
    public void Runtime_projects_follow_the_accepted_reference_direction()
    {
        string solutionRoot = FindSolutionRoot();

        foreach ((string projectPath, string[] expectedReferences) in ExpectedProjectReferences)
        {
            string absoluteProjectPath = Path.Combine(solutionRoot, projectPath);
            XDocument project = XDocument.Load(absoluteProjectPath);
            string projectDirectory = Path.GetDirectoryName(absoluteProjectPath)!;

            string[] actualReferences = project
                .Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(reference => Path.GetFullPath(reference, projectDirectory))
                .Select(reference => Path.GetRelativePath(solutionRoot, reference))
                .Select(reference => reference.Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Harness.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate Harness.slnx from the test output directory.");
    }
}
