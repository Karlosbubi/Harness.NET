using Harness.DataAccess.CodeIntelligence;

namespace Harness.DataAccess.Tests.CodeIntelligence;

public sealed class DotNetSdkSelectorTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-sdk-selector-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Resolves_the_workspace_selected_sdk_to_its_msbuild_path()
    {
        string sdkRoot = Path.Combine(root, "sdk-base");
        string sdkPath = Path.Combine(sdkRoot, "10.0.201");
        Directory.CreateDirectory(sdkPath);
        await File.WriteAllTextAsync(Path.Combine(sdkPath, "MSBuild.dll"), string.Empty);
        FakeDotNetProcess process = new(
            new(0, "10.0.201\n", string.Empty),
            new(0, $"10.0.201 [{sdkRoot}]\n", string.Empty));
        DotNetSdkSelector selector = new(process);

        DotNetSdkSelection result = await selector.SelectAsync(root, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(new DotNetSdkVersion("10.0.201"), result.Version);
        Assert.Equal(new DotNetSdkPath(sdkPath), result.Path);
        Assert.Equal(["--version", "--list-sdks"], process.Arguments);
        Assert.All(process.WorkingDirectories, path => Assert.Equal(root, path));
    }

    [Fact]
    public async Task Reports_an_unavailable_global_json_sdk_as_degraded()
    {
        Directory.CreateDirectory(root);
        DotNetSdkSelector selector = new(new FakeDotNetProcess(
            new DotNetProcessResult(
                145,
                string.Empty,
                "A compatible installed .NET SDK was not found.")));

        DotNetSdkSelection result = await selector.SelectAsync(root, CancellationToken.None);

        Assert.Equal("sdk_unavailable", result.ErrorCode);
        Assert.Contains("compatible", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeDotNetProcess(params DotNetProcessResult[] results) : IDotNetProcess
    {
        private readonly Queue<DotNetProcessResult> pending = new(results);

        internal List<string> Arguments { get; } = [];
        internal List<string> WorkingDirectories { get; } = [];

        public ValueTask<DotNetProcessResult> RunAsync(
            string workingDirectory,
            string argument,
            CancellationToken cancellationToken)
        {
            WorkingDirectories.Add(workingDirectory);
            Arguments.Add(argument);
            return ValueTask.FromResult(pending.Dequeue());
        }
    }
}
