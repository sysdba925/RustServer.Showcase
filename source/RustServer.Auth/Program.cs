using RustServer.Auth.Configuration;
using RustServer.Auth.Infrastructure.Bootstrap;
using RustServer.Auth.Infrastructure.Configuration;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

if (builder.Environment.IsDevelopment())
{
    EnvReader.Load();
}

builder.Services.AddAuthServices(builder.Configuration);

builder.Services.AddControllersWithViews();
builder.Services.AddAuthorization();
builder.Services.AddAuthOpenApi();

var app = builder.Build();

// Nginx terminates TLS before forwarding requests to the loopback listener.
// Only the explicitly configured proxy addresses can supply these headers.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Production traffic reaches Auth through the loopback HTTP listener. TLS is
// terminated by the reverse proxy, so redirecting this listener to HTTPS would
// break Bootstrap's readiness probe and the production deployment contract.
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

await app.InitializeAsync();

app.Run();

public partial class Program;
