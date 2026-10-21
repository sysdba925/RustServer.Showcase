namespace RustServer.Auth.Features.Authentication.CurrentUser;

public sealed record CurrentUserResponse(
    string Id,
    string UserName,
    IReadOnlyList<string> Roles,
    bool GoogleLinked,
    bool SteamLinked);
