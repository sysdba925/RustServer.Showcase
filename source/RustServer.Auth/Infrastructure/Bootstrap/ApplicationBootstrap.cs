namespace RustServer.Auth.Infrastructure.Bootstrap;

public static class ApplicationBootstrap
{
    public static async Task InitializeAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var database = scope.ServiceProvider
            .GetRequiredService<RustServer.Auth.Infrastructure.Persistence.AuthDbContext>();

        try
        {
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions
                .MigrateAsync(database.Database);
        }
        catch (Exception exception)
        {
            app.Logger.LogCritical(
                exception,
                "Authentication database migration failed. Startup is being aborted.");
            throw;
        }

        var roleSeeder = scope.ServiceProvider
            .GetRequiredService<RoleSeeder>();

        await roleSeeder.SeedAsync();

        var adminSeeder = scope.ServiceProvider
            .GetRequiredService<AdminSeeder>();

        await adminSeeder.SeedAsync();
    }
}
