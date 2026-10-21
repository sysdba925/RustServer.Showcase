namespace RustServer.Plugins.Infrastructure.Hosting;

public static class HostingExtensions
{
    public static WebApplicationBuilder ConfigureRustServerHosting(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var pluginsUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_PLUGINS_URL");
        var pluginsBindHost = RequireIpv4BindHost(builder.Configuration, "RUSTSERVER_PLUGINS_BIND_HOST");
        var controlUrl = RequireHttpUrl(builder.Configuration, "RUSTSERVER_CONTROL_URL");

        builder.WebHost.UseUrls($"http://{pluginsBindHost}:{pluginsUrl.Port}");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PluginManagement:Control:BaseUrl"] = controlUrl.AbsoluteUri
        });

        return builder;
    }

    private static string RequireIpv4BindHost(IConfiguration configuration, string key)
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

    private static Uri RequireHttpUrl(IConfiguration configuration, string key)
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
}
