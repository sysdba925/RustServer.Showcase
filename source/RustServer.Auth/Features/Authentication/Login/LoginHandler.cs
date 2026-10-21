using Microsoft.AspNetCore.Identity;
using RustServer.Auth.Infrastructure.Identity;

namespace RustServer.Auth.Features.Authentication.Login;

public sealed class LoginHandler
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LoginHandler(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<bool> HandleAsync(LoginRequest request)
    {
        var result = await _signInManager.PasswordSignInAsync(
            request.UserName,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        return result.Succeeded;
    }
}