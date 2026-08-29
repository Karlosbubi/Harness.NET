using Harness.DataAccess.CodeIntelligence;
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
                <OutputType>Exe</OutputType>
                <Configurations>Debug;Release;Profile</Configurations>
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

        WorkspaceDotNetInfo result = await CreateInspector()
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
        Assert.Equal(DotNetProjectKind.Executable, project.Details!.Kind);
        Assert.True(project.Details.IsStartupCandidate);
        Assert.Equal(["Debug", "Release", "Profile"],
            project.Details.Configurations.Select(configuration => configuration.Name.Value));
        Assert.All(project.Details.Configurations, configuration =>
            Assert.Equal(DotNetConfigurationSource.Declared, configuration.Source));
        Assert.Equal(DotNetSdkHealthState.Ready, result.SdkHealth!.State);
        Assert.Equal("10.0.400", result.SdkHealth.SelectedVersion!.Value);
        Assert.True(result.SdkHealth.WorkloadManifestsAvailable);
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

        WorkspaceDotNetInfo result = await CreateInspector()
            .InspectAsync(root, "Repository.sln");

        Assert.Null(result.Error);
        Assert.Equal("sln", result.EntryPointKind);
        DotNetProjectInfo project = Assert.Single(result.Projects);
        Assert.Equal("Sample.csproj", project.Path);
        Assert.Equal(DotNetProjectKind.Library, project.Details!.Kind);
        Assert.False(project.Details.IsStartupCandidate);
        Assert.Equal(["Debug", "Release"],
            project.Details.Configurations.Select(configuration => configuration.Name.Value));
        Assert.All(project.Details.Configurations, configuration =>
            Assert.Equal(DotNetConfigurationSource.Convention, configuration.Source));
    }

    [Fact]
    public async Task Rejects_an_entry_point_outside_the_workspace()
    {
        Directory.CreateDirectory(root);
        string outside = Path.Combine(Path.GetDirectoryName(root)!, "outside.csproj");
        await File.WriteAllTextAsync(outside, "<Project />");

        WorkspaceDotNetInfo result = await CreateInspector()
            .InspectAsync(root, outside);

        Assert.Equal("outside_workspace", result.ErrorCode);
        File.Delete(outside);
    }

    [Fact]
    public async Task Preserves_valid_projects_while_reporting_each_static_loading_failure()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Repository.slnx"), """
            <Solution>
              <Project Path="Valid.csproj" />
              <Project Path="Missing.csproj" />
              <Project Path="Invalid.csproj" />
              <Project Path="Large.csproj" />
              <Project Path="../Outside.csproj" />
            </Solution>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Valid.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(Path.Combine(root, "Invalid.csproj"), "<Project");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Large.csproj"),
            new string('x', 1024 * 1024 + 1));

        WorkspaceDotNetInfo result = await CreateInspector().InspectAsync(
            root,
            "Repository.slnx");

        Assert.Equal("Valid.csproj", Assert.Single(result.Projects).Path);
        DotNetProjectIssue[] issues = Assert.IsAssignableFrom<IEnumerable<DotNetProjectIssue>>(
            result.ProjectIssues).ToArray();
        Assert.Equal(4, issues.Length);
        Assert.Contains(issues, issue => issue.Kind is DotNetProjectIssueKind.Missing);
        Assert.Contains(issues, issue => issue.Kind is DotNetProjectIssueKind.InvalidMetadata);
        Assert.Contains(issues, issue => issue.Kind is DotNetProjectIssueKind.TooLarge);
        Assert.Contains(issues, issue => issue.Kind is DotNetProjectIssueKind.OutsideWorkspace);
        Assert.All(issues, issue => Assert.DoesNotContain(root, issue.Message, StringComparison.Ordinal));
    }

    private WorkspaceDotNetInspector CreateInspector()
    {
        string sdkBase = Path.Combine(root, ".dotnet", "sdk");
        string sdkPath = Path.Combine(sdkBase, "10.0.400");
        Directory.CreateDirectory(sdkPath);
        File.WriteAllText(Path.Combine(sdkPath, "MSBuild.dll"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, ".dotnet", "sdk-manifests", "10.0.100"));
        return new(new(new DotNetProcess(sdkBase)));
    }

    private sealed class DotNetProcess(string sdkBase) : IDotNetProcess
    {
        public ValueTask<DotNetProcessResult> RunAsync(
            string workingDirectory,
            string argument,
            CancellationToken cancellationToken) => ValueTask.FromResult(argument switch
            {
                "--version" => new DotNetProcessResult(0, "10.0.400\n", string.Empty),
                "--list-sdks" => new DotNetProcessResult(
                    0,
                    $"10.0.400 [{sdkBase}]\n",
                    string.Empty),
                _ => new DotNetProcessResult(1, string.Empty, "Unexpected argument."),
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
