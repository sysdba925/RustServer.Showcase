using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Infrastructure.Persistence;
using RustServer.Auth.Infrastructure.Readiness;

namespace RustServer.Auth.Tests.Infrastructure.Testing;

internal sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string BootstrapAdminName = "test-bootstrap-admin";
    private const string BootstrapAdminPassword = "BootstrapPassword!123";
    private const string GoogleClientId = "test-client-id";
    private const string GoogleClientSecret = "test-client-secret";

    private readonly EnvironmentVariableScope _environment;
    private readonly string _databasePath;
    private readonly string _environmentName;
    private readonly IAuthReadinessProbe? _readinessProbe;

    public AuthWebApplicationFactory(
        IAuthReadinessProbe? readinessProbe = null,
        string environmentName = "Testing")
    {
        _readinessProbe = readinessProbe;
        _environmentName = environmentName;
        _environment = new EnvironmentVariableScope(
            ("ADMIN_USERNAME", BootstrapAdminName),
            ("ADMIN_PASSWORD", BootstrapAdminPassword),
            ("GOOGLE_CLIENT_ID", GoogleClientId),
            ("GOOGLE_CLIENT_SECRET", GoogleClientSecret));

        string databaseDirectory = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Auth.Tests");
        Directory.CreateDirectory(databaseDirectory);

        _databasePath = Path.Combine(databaseDirectory, $"{Guid.NewGuid():N}.db");
    }

    private string ConnectionString => $"Data Source={_databasePath};Pooling=False";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDatabase"] = ConnectionString,
                ["Bootstrap:AdminUserName"] = BootstrapAdminName
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<AuthDbContext>();
            services.AddDbContext<AuthDbContext>(options =>
                options.UseSqlite(ConnectionString));

            if (_readinessProbe is not null)
            {
                services.RemoveAll<IAuthReadinessProbe>();
                services.AddSingleton(_readinessProbe);
            }
        });
    }

    public async Task<ApplicationUser> CreateUserAsync(
        string userName,
        string password)
    {
        using IServiceScope scope = Services.CreateScope();
        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = userName };
        Microsoft.AspNetCore.Identity.IdentityResult result =
            await users.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        return user;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        _environment.Dispose();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
