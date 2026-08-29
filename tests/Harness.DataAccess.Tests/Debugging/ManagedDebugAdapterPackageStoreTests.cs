using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Debugging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.DataAccess.Tests.Debugging;

public sealed class ManagedDebugAdapterPackageStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-debug-adapter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Installs_and_reverifies_only_the_pinned_payload()
    {
        Fixture fixture = CreateFixture();
        ManagedDebugAdapterPackageStore store = CreateStore(fixture);

        StoredDebugAdapterStatus before = await store.GetStatusAsync();
        StoredDebugAdapterStatus installed = await store.InstallAsync();
        string? executable = await store.ResolveVerifiedExecutableAsync();

        Assert.Equal(StoredDebugAdapterAvailability.NotInstalled, before.Availability);
        Assert.Equal(StoredDebugAdapterAvailability.Ready, installed.Availability);
        Assert.NotNull(executable);
        Assert.Equal(fixture.Executable, await File.ReadAllBytesAsync(executable));
        Assert.Equal(fixture.License, await File.ReadAllBytesAsync(
            Path.Combine(Path.GetDirectoryName(executable)!, "LICENSE")));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Contains(fixture.Package.ArchiveUri, fixture.Requests);
        Assert.Contains(fixture.LicenseUri, fixture.Requests);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True((File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) != 0);
        }
    }

    [Fact]
    public async Task Reports_tampering_and_repairs_from_the_verified_archive()
    {
        Fixture fixture = CreateFixture();
        ManagedDebugAdapterPackageStore store = CreateStore(fixture);
        await store.InstallAsync();
        string executable = (await store.ResolveVerifiedExecutableAsync())!;
        await File.WriteAllTextAsync(executable, "tampered");

        StoredDebugAdapterStatus corrupt = await store.GetStatusAsync();
        string? rejected = await store.ResolveVerifiedExecutableAsync();
        StoredDebugAdapterStatus repaired = await store.InstallAsync();

        Assert.Equal(StoredDebugAdapterAvailability.Corrupt, corrupt.Availability);
        Assert.Null(rejected);
        Assert.Equal(StoredDebugAdapterAvailability.Ready, repaired.Availability);
        Assert.Equal(fixture.Executable, await File.ReadAllBytesAsync(
            (await store.ResolveVerifiedExecutableAsync())!));
    }

    [Fact]
    public async Task Rejects_an_archive_before_extracting_when_its_digest_differs()
    {
        Fixture fixture = CreateFixture() with
        {
            Package = CreateFixture().Package with { ArchiveSha256 = new string('0', 64) },
        };
        ManagedDebugAdapterPackageStore store = CreateStore(fixture);

        await Assert.ThrowsAsync<CryptographicException>(async () =>
            await store.InstallAsync());

        Assert.Equal(StoredDebugAdapterAvailability.NotInstalled,
            (await store.GetStatusAsync()).Availability);
    }

    [Fact]
    public async Task Removal_is_confined_to_the_managed_version_directory()
    {
        Fixture fixture = CreateFixture();
        ManagedDebugAdapterPackageStore store = CreateStore(fixture);
        await store.InstallAsync();
        string sentinel = Path.Combine(root, "data", "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");

        StoredDebugAdapterStatus removed = await store.RemoveAsync();

        Assert.Equal(StoredDebugAdapterAvailability.NotInstalled, removed.Availability);
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
        Assert.Null(await store.ResolveVerifiedExecutableAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private ManagedDebugAdapterPackageStore CreateStore(Fixture fixture)
    {
        XdgApplicationPaths paths = new(new(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "state"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "data", "harness.db"),
            Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees")));
        HttpClient client = new(new StubHandler((request, _) =>
        {
            fixture.Requests.Add(request.RequestUri!);
            byte[] content = request.RequestUri == fixture.Package.ArchiveUri
                ? fixture.Archive
                : fixture.License;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }));
        return new(paths, client, NullLogger<ManagedDebugAdapterPackageStore>.Instance,
            fixture.Package, fixture.Package.RuntimeIdentifier, fixture.LicenseUri,
            Sha256(fixture.License));
    }

    private static Fixture CreateFixture()
    {
        byte[] executable = "debugger executable"u8.ToArray();
        byte[] library = "debugger library"u8.ToArray();
        byte[] license = "MIT fixture license"u8.ToArray();
        byte[] archive = CreateTarGzip(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["netcoredbg/netcoredbg"] = executable,
            ["netcoredbg/library.so"] = library,
        });
        Uri archiveUri = new("https://debugger.test/adapter.tar.gz");
        return new(
            new("linux-x64", archiveUri, Sha256(archive), DebugAdapterArchiveKind.TarGzip,
                "netcoredbg",
                [
                    new("netcoredbg", executable.Length, Sha256(executable), true),
                    new("library.so", library.Length, Sha256(library)),
                ]),
            archive,
            executable,
            license,
            new("https://debugger.test/LICENSE"),
            []);
    }

    private static byte[] CreateTarGzip(IReadOnlyDictionary<string, byte[]> files)
    {
        using MemoryStream compressed = new();
        using (GZipStream gzip = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        using (TarWriter writer = new(gzip, leaveOpen: false))
        {
            foreach ((string name, byte[] content) in files)
            {
                PaxTarEntry entry = new(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(content, writable: false),
                };
                writer.WriteEntry(entry);
            }
        }
        return compressed.ToArray();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record Fixture(
        DebugAdapterPackageDefinition Package,
        byte[] Archive,
        byte[] Executable,
        byte[] License,
        Uri LicenseUri,
        List<Uri> Requests);

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
