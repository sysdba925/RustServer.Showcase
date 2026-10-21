using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Features.Authorization;

namespace RustServer.Auth.Infrastructure.Bootstrap;

public sealed class RoleSeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;

    public RoleSeeder(RoleManager<IdentityRole> roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task SeedAsync()
    {
        string[] roles =
        [
            RoleNames.Admin,
            RoleNames.Player
        ];

        foreach (string role in roles)
        {
            if (await _roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await _roleManager.CreateAsync(
                new IdentityRole(role));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ",
                        result.Errors.Select(e => e.Description)));
            }
        }
    }
}