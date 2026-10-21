namespace RustServer.Auth.Features.Authentication.Login;

public sealed record LoginRequest(
    string UserName,
    string Password);