using Microsoft.EntityFrameworkCore;
using RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Development schema initialization requires DOTNET_ENVIRONMENT or ASPNETCORE_ENVIRONMENT to be Development.");
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PluginCatalog");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Set ConnectionStrings__PluginCatalog for the dedicated development database before running this tool.");
}

var options = new DbContextOptionsBuilder<PluginCatalogDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var context = new PluginCatalogDbContext(options);
var created = await context.Database.EnsureCreatedAsync();

Console.WriteLine(created
    ? "Created the development schema."
    : "The development schema already exists; no changes were made.");
