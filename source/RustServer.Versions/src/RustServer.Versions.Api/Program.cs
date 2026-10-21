using Microsoft.OpenApi;
using RustServer.Versions.Api.Versions;

var builder = WebApplication.CreateBuilder(args);
var versionsUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_VERSIONS_URL");
var versionsBindHost = RequireIpv4BindHost(builder.Configuration, "RUSTSERVER_VERSIONS_BIND_HOST");
var rconGrpcUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_RCON_GRPC_URL");

builder.WebHost.UseUrls($"http://{versionsBindHost}:{versionsUrl.Port}");

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("openapi", new OpenApiInfo
    {
        Title = "RustServer Versions API",
        Version = "v1",
        Description = "Retrieves Rust Dedicated Server and Oxide version information."
    });
    options.SupportNonNullableReferenceTypes();
    options.NonNullableReferenceTypesAsRequired();
    options.UseAllOfToExtendReferenceSchemas();
    options.IncludeXmlComments(Path.Combine(
        AppContext.BaseDirectory,
        "RustServer.Versions.Api.xml"));
});
builder.Services.Configure<RustVersionsOptions>(
    builder.Configuration.GetSection(RustVersionsOptions.SectionName));
builder.Services.Configure<OxideVersionsOptions>(
    builder.Configuration.GetSection(OxideVersionsOptions.SectionName));
builder.Services.AddSingleton<IInstalledBuildIdReader, SteamAppManifestBuildIdReader>();
builder.Services.AddSingleton<ISteamAvailableBuildIdReader, SteamKitAvailableBuildIdReader>();
builder.Services.AddSingleton<IRustVersionProvider, RustVersionProvider>();
builder.Services.AddSingleton<IOxideInstalledVersionReader, OxideAssemblyInstalledVersionReader>();
builder.Services.AddSingleton<IOxideRunningVersionReader, RconOxideRunningVersionReader>();
builder.Services.AddSingleton<IRustRunningVersionReader, RconRustRunningVersionReader>();
builder.Services.AddRconGrpcClient(rconGrpcUrl);
builder.Services.AddHttpClient<IOxideAvailableVersionReader, GitHubOxideAvailableVersionReader>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RustServer.Versions/1.0");
});
builder.Services.AddTransient<IOxideVersionProvider, OxideVersionProvider>();

var app = builder.Build();

app.UseSwagger(options => options.RouteTemplate = "{documentName}.json");

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi.json", "RustServer Versions API v1"));
}

app.MapControllers();

app.Run();

static string RequireIpv4BindHost(IConfiguration configuration, string key)
{
    var value = configuration[key]?.Trim();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configuration value '{key}' is required.");
    }

    if (!System.Net.IPAddress.TryParse(value, out var address)
        || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
        || address.ToString() != value)
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' must be a canonical IPv4 bind host.");
    }

    return value;
}

static Uri RequireHttpUrl(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configuration value '{key}' is required.");
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        || !string.IsNullOrEmpty(uri.UserInfo)
        || uri.AbsolutePath != "/"
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException(
            $"Configuration value '{key}' must be an absolute HTTP or HTTPS URL without credentials, a path, query, or fragment.");
    }

    return uri;
}

public partial class Program;
