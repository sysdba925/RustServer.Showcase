using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RustServer.Auth.Features.Authorization;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Infrastructure.Bootstrap;

public sealed class AdminSeeder
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public AdminSeeder(
        UserManager<ApplicationUser> userManager,
         IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task SeedAsync()
    {
        string? bootstrapAdminName = _configuration["Bootstrap:AdminUserName"];

        if (await _userManager.Users.AnyAsync())
        {
            await EnsureExistingBootstrapAdminRolesAsync(bootstrapAdminName);
            return;
        }

        string? userName = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
        string? password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(userName) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Initial admin credentials are missing.");
        }

        if (!string.IsNullOrWhiteSpace(bootstrapAdminName) &&
            !string.Equals(bootstrapAdminName, userName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Bootstrap admin username does not match ADMIN_USERNAME.");
        }

        var admin = new ApplicationUser
        {
            UserName = userName
        };

        var result = await _userManager.CreateAsync(admin, password);
        ValidateResult(result);

        await EnsureAdminRolesAsync(admin);
    }

    private async Task EnsureExistingBootstrapAdminRolesAsync(string? bootstrapAdminName)
    {
        // Initial credentials are intentionally not read after the first user
        // exists. Bootstrap:AdminUserName is optional on subsequent starts;
        // when supplied, it protects against silently selecting another user.
        if (string.IsNullOrWhiteSpace(bootstrapAdminName))
        {
            return;
        }

        var existingAdmin = await _userManager.FindByNameAsync(bootstrapAdminName);

        if (existingAdmin is null)
        {
            throw new InvalidOperationException(
                "Configured bootstrap admin is missing, but users already exist.");
        }

        await EnsureAdminRolesAsync(existingAdmin);
    }

    private async Task EnsureAdminRolesAsync(ApplicationUser admin)
    {
        foreach (string? role in new[] { RoleNames.Admin, RoleNames.Player })
        {
            if (await _userManager.IsInRoleAsync(admin, role))
            {
                continue;
            }

            var result = await _userManager.AddToRoleAsync(admin, role);

            ValidateResult(result);
        }
    }

    private static void ValidateResult(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
