using System.Net;

namespace RustServer.Rcon.Grpc;

internal static class RconGrpcConfiguration
{
    private const string RconUrlConfigurationKey = "RUSTSERVER_RCON_URL";
    private const string GrpcUrlConfigurationKey = "RUSTSERVER_RCON_GRPC_URL";
    private const string DocumentationUrlConfigurationKey = "RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL";

    public static RustServerRconOptions CreateRconOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var endpoint = RequireWebSocketUrl(configuration, RconUrlConfigurationKey);
        var configuredOptions = configuration
            .GetSection(RustServerRconOptions.SectionName)
            .Get<RustServerRconOptions>() ?? new RustServerRconOptions();

        if (string.IsNullOrWhiteSpace(configuredOptions.Password))
        {
            throw new InvalidOperationException(
                $"Configuration value '{RustServerRconOptions.SectionName}:Password' is required.");
        }

        var options = new RustServerRconOptions
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            Password = configuredOptions.Password,
            ConnectTimeout = configuredOptions.ConnectTimeout,
            CommandTimeout = configuredOptions.CommandTimeout,
            MaxResponseBytes = configuredOptions.MaxResponseBytes
        };
        RustServerRconOptionsValidator.Validate(options);

        return options;
    }

    public static RconGrpcListenerOptions CreateListenerOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredOptions = configuration
            .GetSection(RustServerRconGrpcOptions.SectionName)
            .Get<RustServerRconGrpcOptions>() ?? new RustServerRconGrpcOptions();
        var grpcEndpoint = CreateLocalHttpEndpoint(
            configuration[GrpcUrlConfigurationKey] ?? configuredOptions.GrpcListenUrl,
            GrpcUrlConfigurationKey);
        var documentationEndpoint = CreateLocalHttpEndpoint(
            configuration[DocumentationUrlConfigurationKey] ?? configuredOptions.DocumentationListenUrl,
            DocumentationUrlConfigurationKey);

        if (grpcEndpoint.Equals(documentationEndpoint))
        {
            throw new InvalidOperationException(
                $"Configuration values '{GrpcUrlConfigurationKey}' and '{DocumentationUrlConfigurationKey}' must use different addresses.");
        }

        return new RconGrpcListenerOptions(grpcEndpoint, documentationEndpoint);
    }

    private static Uri RequireWebSocketUrl(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is required.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeWs
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"Configuration value '{key}' must be an absolute ws URL without credentials, a path, query, or fragment.");
        }

        return uri;
    }

    private static LocalHttpEndpoint CreateLocalHttpEndpoint(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is required.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.IsLoopback)
        {
            throw new InvalidOperationException(
                $"Configuration value '{key}' must be an absolute localhost HTTP URL without credentials, a path, query, or fragment.");
        }

        var address = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.Parse(uri.Host);

        return new LocalHttpEndpoint(address, uri.Port);
    }
}

internal sealed record RconGrpcListenerOptions(
    LocalHttpEndpoint Grpc,
    LocalHttpEndpoint Documentation);

internal sealed record LocalHttpEndpoint(IPAddress Address, int Port);
