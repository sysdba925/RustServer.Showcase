using Microsoft.AspNetCore.Identity;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using RustServer.Auth.Features.Antiforgery.GetCsrfToken;
using RustServer.Auth.Features.Authentication.CurrentUser;
using RustServer.Auth.Features.Authentication.Google.Link;
using RustServer.Auth.Features.Authentication.Google.Login;
using RustServer.Auth.Features.Authentication.Login;
using RustServer.Auth.Features.Authentication.Logout;
using RustServer.Auth.Features.Authentication.Steam.Link;
using RustServer.Auth.Features.Authentication.Steam.Login;
using RustServer.Auth.Features.Portal.Admin;
using RustServer.Auth.Infrastructure.Authentication;
using RustServer.Auth.Infrastructure.Bootstrap;
using RustServer.Auth.Infrastructure.Identity;
using RustServer.Auth.Infrastructure.Persistence;
using RustServer.Auth.Infrastructure.Readiness;

namespace RustServer.Auth.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services,
        IConfiguration config)
    {
        CookieSecurePolicy cookieSecurePolicy = config.GetValue<bool>("Auth:AllowInsecureHttp")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        AddDbContext(services, config);
        AddIdentity(services);
        ConfigureApplicationCookie(services, cookieSecurePolicy);
        AddAntiforgery(services, cookieSecurePolicy);
        ConfigureForwardedHeaders(services, config);

        services.AddScoped<AdminSeeder>();
        services.AddScoped<RoleSeeder>();
        services.AddScoped<IAuthReadinessProbe, AuthDatabaseReadinessProbe>();

        services.AddScoped<LoginHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<GetCsrfTokenHandler>();
        services.AddAuthentication()
            .AddGoogleProvider()
            .AddSteamProvider();
        services.AddScoped<LinkGoogleHandler>();
        services.AddScoped<GoogleLoginHandler>();
        services.AddScoped<LinkSteamHandler>();
        services.AddScoped<SteamLoginHandler>();
        services.AddScoped<CurrentUserHandler>();
        services.AddScoped<AdminPortalHandler>();

        return services;
    }

    private static void ConfigureForwardedHeaders(
        IServiceCollection services,
        IConfiguration configuration)
    {
        string[] addresses = configuration
            .GetSection("ReverseProxy:TrustedProxies")
            .Get<string[]>() ?? [];

        var trustedProxies = new List<IPAddress>();

        foreach (string address in addresses)
        {
            if (!IPAddress.TryParse(address, out IPAddress? proxyAddress))
            {
                throw new InvalidOperationException(
                    $"ReverseProxy:TrustedProxies contains an invalid IP address: '{address}'.");
            }

            trustedProxies.Add(proxyAddress);
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (IPAddress proxyAddress in trustedProxies)
            {
                options.KnownProxies.Add(proxyAddress);
            }
        });
    }

    private static void AddDbContext(IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("AuthDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The AuthDatabase connection string is not configured.");
        }

        services.AddDbContext<AuthDbContext>(options =>
                    options.UseSqlite(connectionString));
    }

    private static void AddAntiforgery(
        IServiceCollection services,
        CookieSecurePolicy cookieSecurePolicy)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";

            options.Cookie.Name = "RustServer.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
    }

    private static void ConfigureApplicationCookie(
        IServiceCollection services,
        CookieSecurePolicy cookieSecurePolicy)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthenticationCookie.Name;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Lax;

            options.ExpireTimeSpan = TimeSpan.FromHours(8);

            // API must return HTTP status codes, not HTML redirects.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode =
                    StatusCodes.Status401Unauthorized;

                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode =
                    StatusCodes.Status403Forbidden;

                return Task.CompletedTask;
            };
        });
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 12;

            options.Lockout.DefaultLockoutTimeSpan =
                TimeSpan.FromMinutes(60);

            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
                    .AddEntityFrameworkStores<AuthDbContext>()
                    .AddDefaultTokenProviders();
    }
}
