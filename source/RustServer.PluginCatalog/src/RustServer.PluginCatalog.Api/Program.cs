using RustServer.PluginCatalog.Api.CompositionRoot;
using RustServer.PluginCatalog.Api.Features.PluginCatalog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiFoundation();
builder.Services.AddPluginCatalogInfrastructure(builder.Configuration);
builder.Services.AddPluginCatalogFeature();

var app = builder.Build();

app.UseApiExceptionHandling();
app.MapApiEndpoints();

app.Run();

public partial class Program;
