using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace RustServer.Rcon.Grpc.Infrastructure;

internal static class RconGrpcInfrastructureExtensions
{
    public static void ConfigureRconGrpcHost(
        this ConfigureWebHostBuilder webHost,
        RconGrpcListenerOptions listenerOptions)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        ArgumentNullException.ThrowIfNull(listenerOptions);

        webHost.ConfigureKestrel(options =>
        {
            options.Listen(listenerOptions.Grpc.Address, listenerOptions.Grpc.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
            options.Listen(listenerOptions.Documentation.Address, listenerOptions.Documentation.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });
    }

    public static IServiceCollection AddRconGrpcInfrastructure(
        this IServiceCollection services,
        RustServerRconOptions rconOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rconOptions);

        services.AddGrpc()
            .AddJsonTranscoding();
        services.AddSingleton(rconOptions);
        services.AddSingleton<IServerCommandClient, WebRconServerCommandClient>();

        return services;
    }
}
