using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class WorkspaceDotNetInspectorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-dotnet-inspection-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reads_solution_project_and_sdk_metadata_without_evaluation()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Repository.slnx"),
            "<Solution><Project Path=\"src/Sample.csproj\" /></Solution>");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "Sample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Dapper" Version="2.1.79" />
                <ProjectReference Include="../Shared/Shared.csproj" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "global.json"),
            """
            { "sdk": { "version": "10.0.201", "rollForward": "latestPatch", "allowPrerelease": false } }
            """);

        WorkspaceDotNetInfo result = await new WorkspaceDotNetInspector()
            .InspectAsync(root, Path.Combine(root, "Repository.slnx"));

        Assert.Null(result.Error);
        Assert.Equal("slnx", result.EntryPointKind);
        Assert.Equal("10.0.201", result.SdkPolicy!.Version);
        Assert.Equal("latestPatch", result.SdkPolicy.RollForward);
        Assert.False(result.SdkPolicy.AllowPrerelease);
        DotNetProjectInfo project = Assert.Single(result.Projects);
        Assert.Equal("src/Sample.csproj", project.Path);
        Assert.Equal("Microsoft.NET.Sdk", project.Sdk);
        Assert.Equal(["net10.0", "net9.0"], project.TargetFrameworks);
        Assert.Equal("latest", project.LanguageVersion);
        Assert.Equal("enable", project.Nullable);
        Assert.Equal(2, project.References.Count);
    }

    [Fact]
    public async Task Reads_classic_solution_projects_without_msbuild_evaluation()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Repository.sln"),
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Sample", "Sample.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Global
            EndGlobal
            """);

        WorkspaceDotNetInfo result = await new WorkspaceDotNetInspector()
            .InspectAsync(root, "Repository.sln");

        Assert.Null(result.Error);
        Assert.Equal("sln", result.EntryPointKind);
        Assert.Equal("Sample.csproj", Assert.Single(result.Projects).Path);
    }

    [Fact]
    public async Task Rejects_an_entry_point_outside_the_workspace()
    {
        Directory.CreateDirectory(root);
        string outside = Path.Combine(Path.GetDirectoryName(root)!, "outside.csproj");
        await File.WriteAllTextAsync(outside, "<Project />");

        WorkspaceDotNetInfo result = await new WorkspaceDotNetInspector()
            .InspectAsync(root, outside);

        Assert.Equal("outside_workspace", result.ErrorCode);
        File.Delete(outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
