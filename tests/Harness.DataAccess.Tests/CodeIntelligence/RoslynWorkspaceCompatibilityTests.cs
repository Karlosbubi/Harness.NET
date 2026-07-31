using Harness.DataAccess.CodeIntelligence;

namespace Harness.DataAccess.Tests.CodeIntelligence;

[Collection("Roslyn workspace compatibility")]
public sealed class RoslynWorkspaceCompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-roslyn-compatibility-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Loads_csproj_sln_and_slnx_from_the_workspace_sdk_without_restore()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), """
            {
              "sdk": {
                "version": "10.0.201",
                "rollForward": "latestPatch",
                "allowPrerelease": false
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Program.cs"),
            "Console.WriteLine(\"Compatibility checkpoint\");\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Sample.slnx"),
            "<Solution><Project Path=\"Sample.csproj\" /></Solution>");
        await File.WriteAllTextAsync(Path.Combine(root, "Sample.sln"), """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Sample", "Sample.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Global
            EndGlobal
            """);
        IMSBuildRuntime runtime = new MSBuildRuntime(new(new DotNetProcess()));
        IRoslynWorkspaceProbe probe = new RoslynWorkspaceProbe(runtime);

        MSBuildRuntimeResult registration = await runtime.EnsureRegisteredAsync(
            root,
            CancellationToken.None);
        RoslynWorkspaceProbeResult project = await probe.ProbeAsync(
            root, "Sample.csproj", CancellationToken.None);
        RoslynWorkspaceProbeResult solution = await probe.ProbeAsync(
            root, "Sample.sln", CancellationToken.None);
        RoslynWorkspaceProbeResult solutionXml = await probe.ProbeAsync(
            root, "Sample.slnx", CancellationToken.None);

        Assert.Equal(MSBuildRuntimeState.Ready, registration.State);
        Assert.Equal(new DotNetSdkVersion("10.0.201"), registration.SdkVersion);
        AssertLoadsOneProject(project);
        AssertLoadsOneProject(solution);
        AssertLoadsOneProject(solutionXml);
        Assert.False(File.Exists(Path.Combine(root, "obj", "project.assets.json")));
    }

    [Fact]
    public async Task Loads_the_actual_harness_solution_shape()
    {
        string repository = FindRepositoryRoot();
        IRoslynWorkspaceProbe probe = new RoslynWorkspaceProbe(
            new MSBuildRuntime(new(new DotNetProcess())));

        RoslynWorkspaceProbeResult result = await probe.ProbeAsync(
            repository,
            "Harness.slnx",
            CancellationToken.None);

        Assert.NotEqual(RoslynWorkspaceProbeState.Failed, result.State);
        Assert.Equal(new DotNetSdkVersion("10.0.201"), result.SdkVersion);
        Assert.True(result.ProjectCount >= 10, $"Loaded only {result.ProjectCount} projects.");
        Assert.True(result.DocumentCount >= 100, $"Loaded only {result.DocumentCount} documents.");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "workspace_load_failed");
    }

    [Fact]
    public async Task Missing_workspace_sdk_is_reported_as_degraded()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), """
            {
              "sdk": {
                "version": "99.0.100",
                "rollForward": "disable",
                "allowPrerelease": false
              }
            }
            """);
        IMSBuildRuntime runtime = new MSBuildRuntime(new(new DotNetProcess()));

        MSBuildRuntimeResult result = await runtime.EnsureRegisteredAsync(
            root,
            CancellationToken.None);

        Assert.Equal(MSBuildRuntimeState.Degraded, result.State);
        Assert.Equal("sdk_unavailable", result.ErrorCode);
        Assert.Null(result.SdkPath);
    }

    private static void AssertLoadsOneProject(RoslynWorkspaceProbeResult result)
    {
        Assert.NotEqual(RoslynWorkspaceProbeState.Failed, result.State);
        Assert.Equal(1, result.ProjectCount);
        Assert.True(result.DocumentCount >= 1);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "workspace_load_failed");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Harness.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new InvalidOperationException("Harness.slnx was not found above the test output.");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

[CollectionDefinition("Roslyn workspace compatibility", DisableParallelization = true)]
public sealed class RoslynWorkspaceCompatibilityCollection;
