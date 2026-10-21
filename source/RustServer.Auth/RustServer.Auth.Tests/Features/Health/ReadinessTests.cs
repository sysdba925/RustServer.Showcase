using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RustServer.Auth.Infrastructure.Persistence;
using RustServer.Auth.Infrastructure.Readiness;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Features.Health;

public sealed class ReadinessTests
{
    [Fact]
    public async Task GetReady_WhenDatabaseIsMigrated_ReturnsOk()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetReady_WhenProbeReportsUnavailable_ReturnsServiceUnavailable()
    {
        using var factory = new AuthWebApplicationFactory(
            new FixedReadinessProbe(isReady: false));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task IsReadyAsync_WhenSchemaIsMissing_ReturnsFalse()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Auth.Tests",
            $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        try
        {
            var options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using var database = new AuthDbContext(options);
            await database.Database.OpenConnectionAsync();
            await database.Database.CloseConnectionAsync();

            var probe = new AuthDatabaseReadinessProbe(
                database,
                NullLogger<AuthDatabaseReadinessProbe>.Instance);

            Assert.False(await probe.IsReadyAsync(CancellationToken.None));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private sealed class FixedReadinessProbe : IAuthReadinessProbe
    {
        private readonly bool _isReady;

        public FixedReadinessProbe(bool isReady)
        {
            _isReady = isReady;
        }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_isReady);
    }
}
