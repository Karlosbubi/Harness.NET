using System.Text.Json;
using Harness.DataAccess.Research;

namespace Harness.DataAccess.Tests.Research;

public sealed class DependencyEvidenceReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"harness-deps-{Guid.NewGuid():N}");

    public DependencyEvidenceReaderTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task ReadsDeclaredCentralDirectTransitiveAndRestoredEvidenceWithoutRestore()
    {
        string projectDirectory = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "obj"));
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Packages.props"), """
            <Project>
              <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <PackageVersion Include="Dapper" Version="2.1.79"
                                Condition="'$(TargetFramework)' == 'net10.0'" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Dapper" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "App.slnx"), """
            <Solution><Project Path="src/App/App.csproj" /></Solution>
            """);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "obj", "project.assets.json"),
            AssetsJson());

        DependencyEvidenceSnapshot result = await new DependencyEvidenceReader().InspectAsync(
            root, Path.Combine(root, "App.slnx"));

        Assert.Null(result.ErrorCode);
        DependencyProjectEvidence project = Assert.Single(result.Projects);
        Assert.True(project.HasRestoredAssets);
        PackageDependencyEvidence dapper = Assert.Single(project.Packages,
            package => package.Package.Value == "Dapper");
        Assert.Equal("2.1.79", dapper.CentralVersion?.Value);
        Assert.Equal("('$(Configuration)' == 'Debug') && ('$(TargetFramework)' == 'net10.0')",
            dapper.CentralCondition);
        Assert.Equal("2.1.79", dapper.ResolvedVersion?.Value);
        Assert.True(dapper.IsDirect);
        Assert.Contains(DependencyOrigin.Declared, dapper.Origins);
        Assert.Contains(DependencyOrigin.Central, dapper.Origins);
        Assert.Contains(DependencyOrigin.Direct, dapper.Origins);
        Assert.Contains(DependencyOrigin.Restored, dapper.Origins);
        PackageDependencyEvidence transitive = Assert.Single(project.Packages,
            package => package.Package.Value == "System.Data.Common");
        Assert.False(transitive.IsDirect);
        Assert.Contains(DependencyOrigin.Transitive, transitive.Origins);
        Assert.Contains(DependencyOrigin.Restored, transitive.Origins);
        Assert.Contains(dapper.Evidence, evidence => evidence.Value == "Directory.Packages.props");
        Assert.True(File.Exists(Path.Combine(projectDirectory, "obj", "project.assets.json")));
    }

    [Fact]
    public async Task ReportsUnresolvedDeclarationAndDoesNotCreateAssetsFile()
    {
        string project = Path.Combine(root, "App.csproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <PackageReference Include="Serilog" VersionOverride="4.4.0"
                                  Condition="'$(TargetFramework)' == 'net10.0'" />
              </ItemGroup>
            </Project>
            """);

        DependencyEvidenceSnapshot result = await new DependencyEvidenceReader().InspectAsync(root, project);

        DependencyProjectEvidence evidence = Assert.Single(result.Projects);
        Assert.False(evidence.HasRestoredAssets);
        PackageDependencyEvidence package = Assert.Single(evidence.Packages);
        Assert.Equal("4.4.0", package.DeclaredVersion?.Value);
        Assert.Equal("('$(Configuration)' == 'Debug') && ('$(TargetFramework)' == 'net10.0')",
            package.DeclarationCondition);
        Assert.Null(package.ResolvedVersion);
        Assert.False(File.Exists(Path.Combine(root, "obj", "project.assets.json")));
    }

    [Fact]
    public async Task ReportsResolvedVersionConflictsAcrossTargetGraphs()
    {
        string projectDirectory = Path.Combine(root, "App");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "obj"));
        string project = Path.Combine(projectDirectory, "App.csproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks></PropertyGroup>
              <ItemGroup><PackageReference Include="Serilog" Version="[4.0.0,)" /></ItemGroup>
            </Project>
            """);
        object assets = new
        {
            version = 3,
            targets = new Dictionary<string, object>
            {
                ["net9.0"] = new Dictionary<string, object>
                {
                    ["Serilog/4.3.0"] = new { type = "package" },
                },
                ["net10.0"] = new Dictionary<string, object>
                {
                    ["Serilog/4.4.0"] = new { type = "package" },
                },
            },
            libraries = new Dictionary<string, object>
            {
                ["Serilog/4.3.0"] = new { type = "package", sha512 = "old", path = "serilog/4.3.0" },
                ["Serilog/4.4.0"] = new { type = "package", sha512 = "new", path = "serilog/4.4.0" },
            },
        };
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "obj", "project.assets.json"),
            JsonSerializer.Serialize(assets));

        DependencyEvidenceSnapshot result = await new DependencyEvidenceReader().InspectAsync(root, project);

        DependencyConflict conflict = Assert.Single(result.Conflicts,
            item => item.Kind == "resolved_version_conflict");
        Assert.Equal(["4.3.0", "4.4.0"], conflict.Values);
    }

    [Fact]
    public async Task Reads_lock_file_and_reports_conflict_with_restored_graph()
    {
        string projectDirectory = Path.Combine(root, "App");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "obj"));
        string project = Path.Combine(projectDirectory, "App.csproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Serilog" Version="4.4.0" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "obj", "project.assets.json"), """
            { "version": 3,
              "targets": { "net10.0": { "Serilog/4.4.0": { "type": "package" } } },
              "libraries": { "Serilog/4.4.0": { "type": "package", "sha512": "restored", "path": "serilog/4.4.0" } }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "packages.lock.json"), """
            { "version": 2, "dependencies": { "net10.0": {
              "Serilog": { "type": "Direct", "requested": "[4.4.0,)", "resolved": "4.3.0", "contentHash": "locked" }
            } } }
            """);

        DependencyEvidenceSnapshot result = await new DependencyEvidenceReader().InspectAsync(root, project);

        PackageDependencyEvidence locked = Assert.Single(result.Projects[0].Packages,
            package => package.ResolvedVersion?.Value == "4.3.0");
        Assert.Contains(DependencyOrigin.Locked, locked.Origins);
        Assert.Contains(locked.Evidence, evidence => evidence.Value == "App/packages.lock.json");
        Assert.Contains(result.Conflicts,
            conflict => conflict.Kind == "lock_restored_version_conflict");
    }

    private static string AssetsJson() => """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "Dapper/2.1.79": {
                "type": "package",
                "dependencies": { "System.Data.Common": "4.3.0" }
              },
              "System.Data.Common/4.3.0": { "type": "package" }
            }
          },
          "libraries": {
            "Dapper/2.1.79": { "type": "package", "sha512": "dapper-hash", "path": "dapper/2.1.79" },
            "System.Data.Common/4.3.0": { "type": "package", "sha512": "system-hash", "path": "system.data.common/4.3.0" }
          }
        }
        """;

    public void Dispose() => Directory.Delete(root, recursive: true);
}
