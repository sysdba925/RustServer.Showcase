using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Infrastructure.Persistence;

namespace RustServer.Auth.Tests.Infrastructure.Testing;

internal sealed class IdentityTestHost : IAsyncDisposable
{
    private IdentityTestHost(ServiceProvider services, string databasePath)
    {
        Services = services;
        DatabasePath = databasePath;
    }

    public ServiceProvider Services { get; }

    public string DatabasePath { get; }

    public UserManager<ApplicationUser> Users =>
        Services.GetRequiredService<UserManager<ApplicationUser>>();

    public static async Task<IdentityTestHost> CreateAsync(
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        string databaseDirectory = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Auth.Tests");

        Directory.CreateDirectory(databaseDirectory);

        string databasePath = Path.Combine(
            databaseDirectory,
            $"{Guid.NewGuid():N}.db");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AuthDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Pooling=False"));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 12;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        ServiceProvider serviceProvider = services.BuildServiceProvider();

        await using (AsyncServiceScope scope = serviceProvider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await database.Database.EnsureCreatedAsync();

            RoleManager<IdentityRole> roles =
                scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            foreach (string role in new[] { RoleNames.Admin, RoleNames.Player })
            {
                if (!await roles.RoleExistsAsync(role))
                {
                    IdentityResult result = await roles.CreateAsync(new IdentityRole(role));
                    EnsureSuccess(result);
                }
            }
        }

        return new IdentityTestHost(serviceProvider, databasePath);
    }

    public async Task<ApplicationUser> CreateUserAsync(string userName)
    {
        var user = new ApplicationUser { UserName = userName };
        IdentityResult result = await Users.CreateAsync(user, "ValidPassword!123");
        EnsureSuccess(result);

        return user;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}
