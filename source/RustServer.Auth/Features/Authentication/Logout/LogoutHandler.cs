using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Features.Authentication.Logout;

public sealed class LogoutHandler
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LogoutHandler(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task HandleAsync()
    {
        await _signInManager.SignOutAsync();
    }
}