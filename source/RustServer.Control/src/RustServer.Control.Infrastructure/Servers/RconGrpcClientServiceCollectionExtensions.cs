using Microsoft.Extensions.DependencyInjection;
using RustServer.Control.Application.Servers;
using RustServer.Rcon.Grpc;

namespace RustServer.Control.Infrastructure.Servers;

/// <summary>
/// Registers the RCON gRPC client used by Control.
/// </summary>
public static class RconGrpcClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds the standalone RCON gRPC client at the supplied service address.
    /// </summary>
    public static IServiceCollection AddRconGrpcClient(this IServiceCollection services, Uri serviceAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceAddress);

        services.AddGrpcClient<RconService.RconServiceClient>(options => options.Address = serviceAddress);
        services.AddSingleton<IRconClient, RconGrpcClient>();

        return services;
    }
}
