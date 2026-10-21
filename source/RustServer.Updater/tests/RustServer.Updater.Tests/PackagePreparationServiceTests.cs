using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RustServer.Updater.Api.Applications;
using RustServer.Updater.Api.Deployments;
using RustServer.Updater.Api.Operations;

namespace RustServer.Updater.Tests;

public sealed class PackagePreparationServiceTests
{
    private const string ReleaseTag = "v0.1.0-dev.2";
    private static readonly ApplicationRegistration Application = new(
        "Example.Api",
        "owner/repository",
        "/opt/rustserver/example-api",
        "example-api.service",
        "Example.Api.dll");

    [Fact]
    public async Task PrepareAsync_DownloadsPrivateReleaseAndStagesVerifiedPackage()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateArchive(("Example.Api.dll", "assembly"), ("appsettings.json", "{}"));
        var checksum = CreateChecksum(archive, ArchiveName);
        using var transport = new ReleaseHttpHandler(ReleaseMetadata(), archive, checksum);
        using var httpClient = new HttpClient(transport) { BaseAddress = new Uri("https://api.github.test/") };
        var service = CreateService(httpClient, directory.Path);

        var result = await service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None);

        Assert.Equal(Application.Name, result.ApplicationName);
        Assert.Equal(ReleaseTag, result.ReleaseTag);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(archive)), result.VerifiedChecksum);
        Assert.True(File.Exists(Path.Combine(result.StagingPath, Application.Entrypoint)));
        Assert.True(File.Exists(Path.Combine(result.StagingPath, ".prepared-package.json")));
        var manifest = await File.ReadAllTextAsync(Path.Combine(result.StagingPath, ".prepared-package.json"));
        Assert.Contains("\"contents\"", manifest, StringComparison.Ordinal);
        Assert.All(transport.Requests, request =>
            Assert.Equal("test-private-token", request.Authorization?.Parameter));
        Assert.DoesNotContain(Directory.EnumerateDirectories(Path.Combine(directory.Path, ".work"), "*", SearchOption.TopDirectoryOnly), _ => true);
    }

    [Fact]
    public async Task PrepareAsync_AuthContractStagesThePublishedArtifact()
    {
        using var directory = new TemporaryDirectory();
        var application = new ApplicationRegistration(
            "RustServer.Auth",
            "sysdba925/RustServer.Auth",
            "/opt/rustserver/auth",
            "rustserver-auth.service",
            "RustServer.Auth.dll",
            new ApplicationReadinessProbe("http://127.0.0.1:5286/health/ready"));
        var archiveName = $"{application.Name}-{ReleaseTag}-linux-x64.tar.gz";
        var archive = CreateArchive((application.Entrypoint, "assembly"));
        var metadata = $$"""
            {
              "tag_name": "{{ReleaseTag}}",
              "prerelease": true,
              "draft": false,
              "assets": [
                { "name": "{{archiveName}}", "url": "https://api.github.test/repos/sysdba925/RustServer.Auth/releases/assets/1" },
                { "name": "{{archiveName}}.sha256", "url": "https://api.github.test/repos/sysdba925/RustServer.Auth/releases/assets/2" }
              ]
            }
            """;
        using var httpClient = new HttpClient(new ReleaseHttpHandler(metadata, archive, CreateChecksum(archive, archiveName)))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        var result = await service.PrepareAsync(application, ReleaseTag, NoOpStep, CancellationToken.None);

        Assert.Equal(Path.Combine(directory.Path, "RustServer.Auth", ReleaseTag), result.StagingPath);
        Assert.True(File.Exists(Path.Combine(result.StagingPath, "RustServer.Auth.dll")));
        Assert.True(File.Exists(Path.Combine(result.StagingPath, ".prepared-package.json")));
    }

    [Fact]
    public async Task PrepareAsync_StaticWebPreservesManifestAndRejectsTampering()
    {
        using var directory = new TemporaryDirectory();
        var application = new ApplicationRegistration(
            "RustServer.Web",
            "owner/RustServer.Web",
            "/opt/rustserver/web",
            ApplicationType.StaticWeb,
            entryFile: "index.html");
        var archiveName = $"{application.Name}-{ReleaseTag}-linux-x64.tar.gz";
        var archive = CreateArchive(("index.html", "<html>smoke</html>"), ("assets/app.js", "console.log('smoke');"));
        var metadata = $$"""
            {
              "tag_name": "{{ReleaseTag}}",
              "prerelease": true,
              "draft": false,
              "assets": [
                { "name": "{{archiveName}}", "url": "https://api.github.test/repos/owner/RustServer.Web/releases/assets/1" },
                { "name": "{{archiveName}}.sha256", "url": "https://api.github.test/repos/owner/RustServer.Web/releases/assets/2" }
              ]
            }
            """;
        using var httpClient = new HttpClient(new ReleaseHttpHandler(metadata, archive, CreateChecksum(archive, archiveName)))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        var result = await service.PrepareAsync(application, ReleaseTag, NoOpStep, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(result.StagingPath, "index.html")));
        Assert.True(File.Exists(Path.Combine(result.StagingPath, "assets", "app.js")));
        using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.StagingPath, ".prepared-package.json"))))
        {
            var paths = manifest.RootElement.GetProperty("contents").EnumerateArray()
                .Select(entry => entry.GetProperty("path").GetString()!)
                .ToArray();
            Assert.Equal(["assets", "assets/app.js", "index.html"], paths);
        }

        var locator = new PreparedPackageLocator(Options.Create(new PackagePreparationOptions { StoragePath = directory.Path }));
        var verified = await locator.GetAsync(application, ReleaseTag, CancellationToken.None);

        Assert.Equal(result.StagingPath, verified.StagingPath);
        await File.AppendAllTextAsync(Path.Combine(result.StagingPath, "assets", "app.js"), "// tampered");

        await Assert.ThrowsAsync<PreparedPackageNotFoundException>(() =>
            locator.GetAsync(application, ReleaseTag, CancellationToken.None));
    }

    [Fact]
    public async Task PrepareAsync_RejectsMissingCredentialWithoutDisclosingIt()
    {
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new ReleaseHttpHandler(ReleaseMetadata(), [], []))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path, new ThrowingTokenProvider());

        var exception = await Assert.ThrowsAsync<GitHubCredentialUnavailableException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_ReportsRejectedCredentialWithoutToken()
    {
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        var exception = await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-private-token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_RejectsUnknownTagAndDoesNotCreateStaging()
    {
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new StatusHandler(HttpStatusCode.NotFound))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(directory.Path, Application.Name, ReleaseTag)));
    }

    [Fact]
    public async Task PrepareAsync_RejectsTagOutsideConfiguredChannelBeforeNetworkAccess()
    {
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new ThrowingHttpHandler()) { BaseAddress = new Uri("https://api.github.test/") };
        var service = CreateService(httpClient, directory.Path);

        await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, "v0.1.0", NoOpStep, CancellationToken.None));
    }

    [Fact]
    public async Task PrepareAsync_RejectsMissingRequiredAsset()
    {
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new ReleaseHttpHandler("""
            { "tag_name": "v0.1.0-dev.2", "prerelease": true, "draft": false, "assets": [] }
            """, [], [])) { BaseAddress = new Uri("https://api.github.test/") };
        var service = CreateService(httpClient, directory.Path);

        var exception = await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.Contains("missing required asset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_RejectsWrongChecksumAndCleansWorkDirectory()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateArchive(("Example.Api.dll", "assembly"));
        var badChecksum = new string('0', 64) + "  " + ArchiveName + "\n";
        using var httpClient = new HttpClient(new ReleaseHttpHandler(ReleaseMetadata(), archive, Encoding.UTF8.GetBytes(badChecksum)))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(directory.Path, Application.Name, ReleaseTag)));
        Assert.Empty(Directory.Exists(Path.Combine(directory.Path, ".work"))
            ? Directory.EnumerateFileSystemEntries(Path.Combine(directory.Path, ".work"))
            : []);
    }

    [Fact]
    public async Task PrepareAsync_HandlesDownloadFailureWithoutLeavingStaging()
    {
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new AssetFailureHandler(ReleaseMetadata()))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(directory.Path, Application.Name, ReleaseTag)));
    }

    [Fact]
    public async Task GitHubReleaseClient_FollowsHttpsAssetRedirectWithoutForwardingAuthorization()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "asset.bin");
        var handler = new RedirectHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") };
        var client = new GitHubReleaseClient(httpClient, new FixedTokenProvider());

        await client.DownloadAssetAsync(
            new Uri("https://api.github.test/repos/owner/repository/releases/assets/1"),
            destination,
            1024,
            CancellationToken.None);

        Assert.Equal("payload", await File.ReadAllTextAsync(destination));
        Assert.Equal("test-private-token", handler.Requests[0].Authorization?.Parameter);
        Assert.Null(handler.Requests[1].Authorization);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("traversal")]
    [InlineData("symlink")]
    [InlineData("entrypoint")]
    public async Task PrepareAsync_RejectsInvalidOrUnsafeArchive(string scenario)
    {
        using var directory = new TemporaryDirectory();
        var archive = scenario switch
        {
            "corrupt" => Encoding.UTF8.GetBytes("not a gzip archive"),
            "traversal" => CreateArchive(("../escape.dll", "assembly")),
            "symlink" => CreateArchiveWithSymlink(),
            "entrypoint" => CreateArchive(("Other.dll", "assembly")),
            _ => throw new InvalidOperationException()
        };
        using var httpClient = new HttpClient(new ReleaseHttpHandler(ReleaseMetadata(), archive, CreateChecksum(archive, ArchiveName)))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);

        await Assert.ThrowsAsync<PackagePreparationException>(() =>
            service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(directory.Path, Application.Name, ReleaseTag)));
    }

    [Fact]
    public async Task PrepareAsync_IsIdempotentAndCanReuseStagingAfterServiceRestart()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateArchive(("Example.Api.dll", "assembly"));
        var checksum = CreateChecksum(archive, ArchiveName);
        using var httpClient = new HttpClient(new ReleaseHttpHandler(ReleaseMetadata(), archive, checksum))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };

        var first = await CreateService(httpClient, directory.Path)
            .PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None);
        var second = await CreateService(httpClient, directory.Path)
            .PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None);

        Assert.Equal(first.StagingPath, second.StagingPath);
        Assert.Equal(first.VerifiedChecksum, second.VerifiedChecksum);
        Assert.True(File.Exists(Path.Combine(second.StagingPath, Application.Entrypoint)));
    }

    [Fact]
    public async Task PreparedPackageLocator_RejectsStagingContentChangedAfterStage6Verification()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateArchive(("Example.Api.dll", "assembly"), ("appsettings.json", "{}"));
        using var httpClient = new HttpClient(new ReleaseHttpHandler(ReleaseMetadata(), archive, CreateChecksum(archive, ArchiveName)))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var service = CreateService(httpClient, directory.Path);
        var result = await service.PrepareAsync(Application, ReleaseTag, NoOpStep, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(result.StagingPath, "Example.Api.dll"), "replaced");
        var locator = new PreparedPackageLocator(Options.Create(new PackagePreparationOptions { StoragePath = directory.Path }));

        var exception = await Assert.ThrowsAsync<PreparedPackageNotFoundException>(() =>
            locator.GetAsync(Application, ReleaseTag, CancellationToken.None));

        Assert.Contains("contents", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handler_RejectsConcurrentPreparationAndPersistsResult()
    {
        using var directory = new TemporaryDirectory();
        var archive = CreateArchive(("Example.Api.dll", "assembly"));
        using var httpClient = new HttpClient(new ReleaseHttpHandler(ReleaseMetadata(), archive, CreateChecksum(archive, ArchiveName)))
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var registry = new InMemoryApplicationRegistry(Application);
        var operations = new OperationManager(new InMemoryOperationStore(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OperationManager>.Instance);
        var queue = new CapturingQueue();
        var handler = new PrepareApplicationHandler(
            registry,
            operations,
            queue,
            CreateService(httpClient, directory.Path),
            new SuccessfulPrepareExecutor());

        var accepted = await handler.HandleAsync(Application.Name, new PrepareApplicationRequest(ReleaseTag), CancellationToken.None);
        await Assert.ThrowsAsync<OperationConflictException>(() =>
            handler.HandleAsync(Application.Name, new PrepareApplicationRequest(ReleaseTag), CancellationToken.None));
        await queue.RunNextAsync();
        var completed = await operations.GetAsync(accepted.Id, CancellationToken.None);

        Assert.NotNull(completed);
        Assert.Equal(OperationStatus.Succeeded, completed.Status);
        Assert.Equal("Verified package secured", completed.Step);
        Assert.NotNull(completed.Result);
        Assert.Equal(ReleaseTag, completed.Result.ReleaseTag);
    }

    private static PackagePreparationService CreateService(
        HttpClient httpClient,
        string storagePath,
        IGitHubTokenProvider? tokenProvider = null) => new(
        new GitHubReleaseClient(httpClient, tokenProvider ?? new FixedTokenProvider()),
        new FixedReleaseChannelProvider(),
        Options.Create(new PackagePreparationOptions { StoragePath = storagePath }));

    private static Task NoOpStep(string step, CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed class SuccessfulPrepareExecutor : IDeploymentExecutorClient
    {
        public Task<DeploymentTransaction> ExecuteAsync(
            DeploymentExecutorRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DeploymentTransaction(
                request.OperationId,
                request.ApplicationName,
                DeploymentTransactionStatus.Succeeded,
                "Verified package secured",
                null,
                request.TargetVersion,
                null,
                null,
                DateTimeOffset.UtcNow));

        public Task<DeploymentTransaction?> GetTransactionAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<DeploymentTransaction?>(null);
    }

    private static string ArchiveName => $"{Application.Name}-{ReleaseTag}-linux-x64.tar.gz";

    private static string ReleaseMetadata() => $$"""
        {
          "tag_name": "{{ReleaseTag}}",
          "prerelease": true,
          "draft": false,
          "assets": [
            { "name": "{{ArchiveName}}", "url": "https://api.github.test/repos/owner/repository/releases/assets/1", "browser_download_url": "https://invalid.example/archive" },
            { "name": "{{ArchiveName}}.sha256", "url": "https://api.github.test/repos/owner/repository/releases/assets/2", "browser_download_url": "https://invalid.example/checksum" }
          ]
        }
        """;

    private static byte[] CreateChecksum(byte[] archive, string archiveName) =>
        Encoding.ASCII.GetBytes($"{Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant()}  {archiveName}\n");

    private static byte[] CreateArchive(params (string Name, string Contents)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, contents) in files)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(contents))
                });
            }
        }

        return output.ToArray();
    }

    private static byte[] CreateArchiveWithSymlink()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "Example.Api.dll") { LinkName = "elsewhere.dll" });
        }

        return output.ToArray();
    }

    private sealed class FixedTokenProvider : IGitHubTokenProvider
    {
        public string GetToken() => "test-private-token";
    }

    private sealed class ThrowingTokenProvider : IGitHubTokenProvider
    {
        public string GetToken() => throw new GitHubCredentialUnavailableException("Updater GitHub credential is unavailable.");
    }

    private sealed class FixedReleaseChannelProvider : IReleaseChannelProvider
    {
        public ReleaseChannel GetChannel() => ReleaseChannel.Development;
    }

    private sealed class CapturingQueue : IBackgroundOperationQueue
    {
        private readonly Queue<Func<CancellationToken, Task>> _work = [];

        public void Enqueue(Func<CancellationToken, Task> work) => _work.Enqueue(work);

        public Task RunNextAsync() => _work.Dequeue()(CancellationToken.None);
    }

    private sealed class ReleaseHttpHandler(string metadata, byte[] archive, byte[] checksum) : HttpMessageHandler
    {
        public List<(Uri Uri, AuthenticationHeaderValue? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization));
            var path = request.RequestUri!.AbsolutePath;
            var response = path.EndsWith($"/tags/{ReleaseTag}", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata, Encoding.UTF8, "application/json") }
                : path.EndsWith("/assets/1", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }
                    : path.EndsWith("/assets/2", StringComparison.Ordinal)
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(checksum) }
                        : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("The HTTP client should not have been called.");
    }

    private sealed class AssetFailureHandler(string metadata) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/tags/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public List<(Uri Uri, AuthenticationHeaderValue? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization));
            if (request.RequestUri!.Host == "api.github.test")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("https://objects.github.test/download/1") }
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("payload")
            });
        }
    }
}
