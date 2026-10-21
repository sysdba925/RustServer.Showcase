using RustServer.Plugins.Features.PluginManagement;
using RustServer.Plugins.Infrastructure.Hosting;
using RustServer.Plugins.Infrastructure.Http;
using RustServer.Plugins.Infrastructure.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureRustServerHosting();

builder.Services.AddControllers();
builder.Services.AddApiDocumentation(builder.Configuration);
builder.Services.AddPluginManagement(builder.Configuration);
builder.Services.AddOperationHistoryExceptionHandling();

var app = builder.Build();

app.UseExceptionHandler();
app.UseApiDocumentation();
app.MapControllers();

app.Run();

public partial class Program;
