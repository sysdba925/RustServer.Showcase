namespace RustServer.Rcon.Grpc;

/// <summary>
/// Configures the local endpoints exposed by the RCON gRPC host.
/// </summary>
public sealed class RustServerRconGrpcOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "RustServerRconGrpc";

    /// <summary>
    /// The HTTP/2 endpoint used by gRPC clients.
    /// </summary>
    public string GrpcListenUrl { get; init; } = "http://127.0.0.1:50051";

    /// <summary>
    /// The HTTP/1.1 endpoint used for OpenAPI and Swagger UI.
    /// </summary>
    public string DocumentationListenUrl { get; init; } = "http://127.0.0.1:50052";
}
