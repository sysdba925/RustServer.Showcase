using RustServer.Updater.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUpdaterServices(builder.Configuration);

var app = builder.Build();

app.MapControllers();
app.MapUpdaterEndpoints();

app.Run();

public partial class Program;
