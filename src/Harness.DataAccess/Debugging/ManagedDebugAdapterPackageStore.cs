using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Harness.DataAccess.Configuration;
using Microsoft.Extensions.Logging;

namespace Harness.DataAccess.Debugging;

internal sealed class ManagedDebugAdapterPackageStore :
    IDebugAdapterPackageStore, IDebugAdapterExecutableResolver
{
    private const long MaximumArchiveBytes = 8 * 1024 * 1024;
    private const long MaximumLicenseBytes = 64 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IApplicationPaths applicationPaths;
    private readonly HttpClient httpClient;
    private readonly ILogger<ManagedDebugAdapterPackageStore> logger;
    private readonly DebugAdapterPackageDefinition? packageOverride;
    private readonly string runtimeIdentifier;
    private readonly Uri licenseUri;
    private readonly string licenseSha256;

    public ManagedDebugAdapterPackageStore(
        IApplicationPaths applicationPaths,
        HttpClient httpClient,
        ILogger<ManagedDebugAdapterPackageStore> logger)
        : this(applicationPaths, httpClient, logger, null,
            DebugAdapterPackageCatalog.CurrentRuntimeIdentifier,
            DebugAdapterPackageCatalog.LicenseUri,
            DebugAdapterPackageCatalog.LicenseSha256)
    {
    }

    internal ManagedDebugAdapterPackageStore(
        IApplicationPaths applicationPaths,
        HttpClient httpClient,
        ILogger<ManagedDebugAdapterPackageStore> logger,
        DebugAdapterPackageDefinition? packageOverride,
        string runtimeIdentifier,
        Uri licenseUri,
        string licenseSha256)
    {
        this.applicationPaths = applicationPaths;
        this.httpClient = httpClient;
        this.logger = logger;
        this.packageOverride = packageOverride;
        this.runtimeIdentifier = runtimeIdentifier;
        this.licenseUri = licenseUri;
        this.licenseSha256 = licenseSha256;
    }

    public async ValueTask<StoredDebugAdapterStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetPackage(out DebugAdapterPackageDefinition? package) || package is null)
        {
            return Status(StoredDebugAdapterAvailability.Unsupported,
                "The pinned debugger has no package for this operating system and architecture.",
                canInstall: false, canRemove: false);
        }

        string directory = InstallDirectory(package);
        if (!Directory.Exists(directory))
        {
            return Status(StoredDebugAdapterAvailability.NotInstalled,
                "The managed debugger is not installed.", canInstall: true, canRemove: false);
        }

        bool valid = await VerifyInstallationAsync(package, directory, cancellationToken);
        return valid
            ? Status(StoredDebugAdapterAvailability.Ready,
                "The managed debugger is installed and every payload digest is verified.",
                canInstall: false, canRemove: true)
            : Status(StoredDebugAdapterAvailability.Corrupt,
                "The managed debugger payload failed integrity verification.",
                canInstall: true, canRemove: true);
    }

    public async ValueTask<StoredDebugAdapterStatus> InstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetPackage(out DebugAdapterPackageDefinition? package) || package is null)
        {
            return Status(StoredDebugAdapterAvailability.Unsupported,
                "The pinned debugger has no package for this operating system and architecture.",
                canInstall: false, canRemove: false);
        }

        await gate.WaitAsync(cancellationToken);
        string? temporaryRoot = null;
        try
        {
            StoredDebugAdapterStatus current = await GetStatusAsync(cancellationToken);
            if (current.Availability is StoredDebugAdapterAvailability.Ready)
            {
                return current;
            }

            string cacheRoot = Path.Combine(applicationPaths.Current.CacheDirectory, "debuggers");
            Directory.CreateDirectory(cacheRoot);
            temporaryRoot = Path.Combine(cacheRoot, $"install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryRoot);
            string archivePath = Path.Combine(temporaryRoot, "adapter-package");
            string licensePath = Path.Combine(temporaryRoot, "LICENSE");
            string payloadDirectory = Path.Combine(temporaryRoot, "payload");
            Directory.CreateDirectory(payloadDirectory);

            await DownloadBoundedAsync(package.ArchiveUri, archivePath, MaximumArchiveBytes,
                cancellationToken);
            await VerifyFileAsync(archivePath, null, package.ArchiveSha256, cancellationToken);
            await ExtractAsync(package, archivePath, payloadDirectory, cancellationToken);
            await DownloadBoundedAsync(licenseUri, licensePath,
                MaximumLicenseBytes, cancellationToken);
            await VerifyFileAsync(licensePath, null, licenseSha256,
                cancellationToken);
            File.Move(licensePath, Path.Combine(payloadDirectory, "LICENSE"));
            await VerifyPayloadAsync(package, payloadDirectory, cancellationToken);

            string installDirectory = InstallDirectory(package);
            Directory.CreateDirectory(Path.GetDirectoryName(installDirectory)!);
            if (Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, recursive: true);
            }
            Directory.Move(payloadDirectory, installDirectory);

            if (!OperatingSystem.IsWindows())
            {
                string executable = Path.Combine(installDirectory, package.ExecutableName);
                File.SetUnixFileMode(executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            if (!await VerifyInstallationAsync(package, installDirectory, cancellationToken))
            {
                throw new InvalidDataException(
                    "The installed debugger did not pass post-install integrity verification.");
            }

            logger.LogInformation(
                "Installed verified NetCoreDbg {Version} for {RuntimeIdentifier}",
                DebugAdapterPackageCatalog.Version,
                package.RuntimeIdentifier);
            return Status(StoredDebugAdapterAvailability.Ready,
                "The managed debugger is installed and every payload digest is verified.",
                canInstall: false, canRemove: true);
        }
        finally
        {
            if (temporaryRoot is not null && Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
            gate.Release();
        }
    }

    public async ValueTask<StoredDebugAdapterStatus> RemoveAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetPackage(out DebugAdapterPackageDefinition? package) || package is null)
        {
            return Status(StoredDebugAdapterAvailability.Unsupported,
                "The pinned debugger has no package for this operating system and architecture.",
                canInstall: false, canRemove: false);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = InstallDirectory(package);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            return Status(StoredDebugAdapterAvailability.NotInstalled,
                "The managed debugger is not installed.", canInstall: true, canRemove: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<string?> ResolveVerifiedExecutableAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetPackage(out DebugAdapterPackageDefinition? package) || package is null)
        {
            return null;
        }

        string directory = InstallDirectory(package);
        return await VerifyInstallationAsync(package, directory, cancellationToken)
            ? Path.Combine(directory, package.ExecutableName)
            : null;
    }

    private async Task DownloadBoundedAsync(
        Uri uri,
        string destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Harness.NET/1.0");
        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                $"Debugger download returned HTTP {(int)response.StatusCode}.");
        }
        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength > maximumBytes)
        {
            throw new InvalidDataException("Debugger download exceeded its size limit.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Debugger download exceeded its size limit.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await target.FlushAsync(cancellationToken);
    }

    private static async Task ExtractAsync(
        DebugAdapterPackageDefinition package,
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        HashSet<string> expected = package.Payloads
            .Select(payload => $"netcoredbg/{payload.Name}")
            .ToHashSet(StringComparer.Ordinal);
        switch (package.ArchiveKind)
        {
            case DebugAdapterArchiveKind.TarGzip:
                await using (FileStream file = File.OpenRead(archivePath))
                await using (GZipStream gzip = new(file, CompressionMode.Decompress))
                using (TarReader reader = new(gzip, leaveOpen: false))
                {
                    while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
                    {
                        string name = entry.Name.Replace('\\', '/');
                        if (!expected.Contains(name)) continue;
                        if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null)
                            throw new InvalidDataException("The debugger archive contains an invalid payload entry.");
                        await CopyEntryAsync(entry.DataStream,
                            Path.Combine(destination, Path.GetFileName(name)), cancellationToken);
                    }
                }
                break;
            case DebugAdapterArchiveKind.Zip:
                using (ZipArchive archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (!expected.Contains(name)) continue;
                        await using Stream source = entry.Open();
                        await CopyEntryAsync(source,
                            Path.Combine(destination, Path.GetFileName(name)), cancellationToken);
                    }
                }
                break;
            default:
                throw new InvalidOperationException("Unsupported debugger archive kind.");
        }
    }

    private static async Task CopyEntryAsync(
        Stream source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream target = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(target, cancellationToken);
    }

    private async Task<bool> VerifyInstallationAsync(
        DebugAdapterPackageDefinition package,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(directory) || IsLink(directory)) return false;
            await VerifyPayloadAsync(package, directory, cancellationToken);
            await VerifyFileAsync(Path.Combine(directory, "LICENSE"), null,
                licenseSha256, cancellationToken);
            string[] files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
            return files.Length == package.Payloads.Count + 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or CryptographicException)
        {
            return false;
        }
    }

    private static async Task VerifyPayloadAsync(
        DebugAdapterPackageDefinition package,
        string directory,
        CancellationToken cancellationToken)
    {
        foreach (DebugAdapterPayload payload in package.Payloads)
        {
            await VerifyFileAsync(Path.Combine(directory, payload.Name), payload.Bytes,
                payload.Sha256, cancellationToken);
        }
    }

    private static async Task VerifyFileAsync(
        string path,
        long? expectedBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (!file.Exists || IsLink(path) || (expectedBytes is not null && file.Length != expectedBytes))
            throw new InvalidDataException("A debugger payload is missing or has an invalid size.");
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!Convert.ToHexStringLower(digest).Equals(expectedSha256, StringComparison.Ordinal))
            throw new CryptographicException("A debugger payload digest did not match.");
    }

    private string InstallDirectory(DebugAdapterPackageDefinition package) =>
        Path.Combine(applicationPaths.Current.DataDirectory, "debuggers", "netcoredbg",
            DebugAdapterPackageCatalog.Version, package.RuntimeIdentifier);

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private bool TryGetPackage(out DebugAdapterPackageDefinition? package)
    {
        if (packageOverride is not null)
        {
            package = packageOverride;
            return true;
        }
        return DebugAdapterPackageCatalog.TryGetCurrent(out package);
    }

    private StoredDebugAdapterStatus Status(
        StoredDebugAdapterAvailability availability,
        string summary,
        bool canInstall,
        bool canRemove) =>
        new(availability, new(DebugAdapterPackageCatalog.Version),
            new(runtimeIdentifier), summary, canInstall, canRemove);
}
