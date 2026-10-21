using Microsoft.Extensions.Configuration;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Bootstrap;
using RustServer.Auth.Tests.Infrastructure.Testing;

namespace RustServer.Auth.Tests.Infrastructure.Bootstrap;

public sealed class AdminSeederTests
{
    private const string AdminUserName = "initial-admin";
    private const string AdminPassword = "AdminPassword!123";

    [Fact]
    public async Task SeedAsync_NewAdmin_AssignsAdminAndPlayerRoles()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        using var environment = CreateAdminEnvironment();

        var seeder = new AdminSeeder(host.Users, CreateConfiguration());

        await seeder.SeedAsync();

        var admin = await host.Users.FindByNameAsync(AdminUserName);

        Assert.NotNull(admin);
        Assert.True(await host.Users.IsInRoleAsync(admin, RoleNames.Admin));
        Assert.True(await host.Users.IsInRoleAsync(admin, RoleNames.Player));
    }

    [Fact]
    public async Task SeedAsync_ExistingAdmin_RestoresMissingRoles()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        using var environment = CreateAdminEnvironment();
        var existingAdmin = await host.CreateUserAsync(AdminUserName);

        Assert.True(
            (await host.Users.AddToRoleAsync(existingAdmin, RoleNames.Player)).Succeeded);

        var seeder = new AdminSeeder(host.Users, CreateConfiguration());

        await seeder.SeedAsync();

        Assert.True(await host.Users.IsInRoleAsync(existingAdmin, RoleNames.Admin));
        Assert.True(await host.Users.IsInRoleAsync(existingAdmin, RoleNames.Player));
    }

    [Fact]
    public async Task SeedAsync_ExistingAdmin_DoesNotRequireInitialCredentials()
    {
        await using IdentityTestHost host = await IdentityTestHost.CreateAsync();
        var existingAdmin = await host.CreateUserAsync(AdminUserName);
        Assert.True(
            (await host.Users.AddToRoleAsync(existingAdmin, RoleNames.Player)).Succeeded);

        var seeder = new AdminSeeder(host.Users, CreateConfiguration());

        await seeder.SeedAsync();

        Assert.True(await host.Users.IsInRoleAsync(existingAdmin, RoleNames.Admin));
        Assert.True(await host.Users.IsInRoleAsync(existingAdmin, RoleNames.Player));
    }

    private static EnvironmentVariableScope CreateAdminEnvironment() =>
        new(
            ("ADMIN_USERNAME", AdminUserName),
            ("ADMIN_PASSWORD", AdminPassword));

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:AdminUserName"] = AdminUserName
            })
            .Build();
}
