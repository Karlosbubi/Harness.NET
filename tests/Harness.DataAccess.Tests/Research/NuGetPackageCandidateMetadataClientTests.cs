using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Research;

namespace Harness.DataAccess.Tests.Research;

public sealed class NuGetPackageCandidateMetadataClientTests
{
    [Fact]
    public async Task Reads_exact_registration_manifest_assets_advisory_provenance_and_integrity()
    {
        byte[] package = PackageArchive();
        string hash = Convert.ToBase64String(SHA512.HashData(package));
        FakeHandler handler = new(package, hash);
        NuGetPackageCandidateMetadataClient client = new(new HttpClient(handler));

        PackageCandidateMetadata result = Assert.Single(await client.GetAsync(new(
            new("Example.Package"),
            new("2.0.0"),
            [new("net10.0")],
            [new("linux-x64")],
            AllowPrerelease: false,
            [new(new Uri("https://packages.example.test/v3/index.json"))])));

        Assert.True(result.Exists);
        Assert.Equal("MIT", result.LicenseExpression);
        Assert.Equal("https://github.example.test/example", result.RepositoryUrl?.AbsoluteUri);
        Assert.Equal("abc123", result.RepositoryCommit);
        Assert.Equal(hash, result.PublishedSha512);
        Assert.Equal(hash, result.ComputedSha512);
        Assert.True(Assert.Single(result.Compatibility).IsCompatible);
        Assert.True(Assert.Single(result.RuntimeCompatibility).IsCompatible);
        Assert.Equal("Dependency", Assert.Single(result.Dependencies).Package.Value);
        Assert.Equal(2, Assert.Single(result.Advisories).Severity);
        Assert.True(result.IsDeprecated);
        Assert.True(handler.Requests.All(uri => uri.Scheme == Uri.UriSchemeHttps));
    }

    [Fact]
    public async Task Missing_exact_version_is_reported_without_downloading_archive()
    {
        FakeHandler handler = new([], string.Empty) { RegistrationStatus = HttpStatusCode.NotFound };
        NuGetPackageCandidateMetadataClient client = new(new HttpClient(handler));

        PackageCandidateMetadata result = Assert.Single(await client.GetAsync(new(
            new("Missing"), new("1.0.0"), [], [], false,
            [new(new Uri("https://packages.example.test/v3/index.json"))])));

        Assert.False(result.Exists);
        Assert.Equal("package_version_not_found", result.ErrorCode);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.EndsWith(".nupkg"));
    }

    private static byte[] PackageArchive()
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "Example.Package.nuspec", """
                <?xml version="1.0"?>
                <package><metadata>
                  <id>Example.Package</id><version>2.0.0</version>
                  <license type="expression">MIT</license>
                  <projectUrl>https://example.test/project</projectUrl>
                  <repository type="git" url="https://github.example.test/example" commit="abc123" />
                  <dependencies><group targetFramework="net10.0">
                    <dependency id="Dependency" version="[1.0.0,2.0.0)" />
                  </group></dependencies>
                </metadata></package>
                """);
            Write(archive, "lib/net10.0/Example.Package.dll", "fake assembly bytes");
            Write(archive, "runtimes/linux-x64/lib/net10.0/Example.Package.Native.dll", "fake native bytes");
        }
        return stream.ToArray();
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class FakeHandler(byte[] package, string hash) : HttpMessageHandler
    {
        internal HttpStatusCode RegistrationStatus { get; init; } = HttpStatusCode.OK;
        internal List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            if (uri.AbsolutePath.EndsWith("/v3/index.json", StringComparison.Ordinal))
            {
                return Json("""
                    { "resources": [
                      { "@id": "https://packages.example.test/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                      { "@id": "https://packages.example.test/content/", "@type": "PackageBaseAddress/3.0.0" }
                    ] }
                    """);
            }
            if (uri.AbsolutePath.Contains("/registration/", StringComparison.Ordinal))
            {
                if (RegistrationStatus != HttpStatusCode.OK)
                {
                    return Task.FromResult(new HttpResponseMessage(RegistrationStatus));
                }
                return Json("""
                    {
                      "catalogEntry": {
                        "listed": true,
                        "licenseExpression": "MIT",
                        "projectUrl": "https://example.test/project",
                        "repository": { "url": "https://github.example.test/example", "commit": "abc123" },
                        "deprecation": { "message": "Use the successor" },
                        "dependencyGroups": [ { "targetFramework": "net10.0", "dependencies": [
                          { "id": "Dependency", "range": "[1.0.0,2.0.0)" }
                        ] } ],
                        "vulnerabilities": [ { "advisoryUrl": "https://advisories.example.test/1", "severity": 2 } ]
                      }
                    }
                    """);
            }
            if (uri.AbsolutePath.EndsWith(".nupkg.sha512", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(hash),
                });
            }
            if (uri.AbsolutePath.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(package),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string value) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(value, Encoding.UTF8, "application/json"),
            });
    }
}
