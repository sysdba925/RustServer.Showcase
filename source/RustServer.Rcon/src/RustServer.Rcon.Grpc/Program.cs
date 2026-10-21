using RustServer.Rcon.Grpc;
using RustServer.Rcon.Grpc.Infrastructure;
using RustServer.Rcon.Grpc.Infrastructure.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var rconOptions = RconGrpcConfiguration.CreateRconOptions(builder.Configuration);
var listenerOptions = RconGrpcConfiguration.CreateListenerOptions(builder.Configuration);

builder.WebHost.ConfigureRconGrpcHost(listenerOptions);
builder.Services.AddRconGrpcInfrastructure(rconOptions);
builder.Services.AddRconGrpcOpenApi();

var app = builder.Build();

app.UseRconGrpcOpenApi();
app.MapGet("/health", () => Results.Ok())
    .WithTags("Service")
    .WithSummary("Reports service availability")
    .WithDescription("Returns OK when the RCON service is running and can respond to HTTP requests.");
app.MapGrpcService<RconGrpcService>();

app.Run();

public partial class Program;
