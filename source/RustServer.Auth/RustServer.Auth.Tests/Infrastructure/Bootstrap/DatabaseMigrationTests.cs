using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Auth.Infrastructure.Persistence;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Infrastructure.Bootstrap;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task Startup_AppliesAllPendingMigrations()
    {
        using var factory = new AuthWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        using IServiceScope scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
    }
}
