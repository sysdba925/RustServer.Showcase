using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Text.Json.Nodes;

namespace RustServer.Plugins.ApiContractGenerator;

internal static class Program
{
    private const string OpenApiFileName = "openapi.json";

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("Usage: dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- [--verify]");
            return 0;
        }

        if (args.Length > 1 || (args.Length == 1 && args[0] != "--verify"))
        {
            Console.Error.WriteLine("Unknown argument. Use --verify to validate committed openapi.json without writing it.");
            return 2;
        }

        var repositoryRoot = FindRepositoryRoot();
        ConfigureTopologyEnvironment();

        using var factory = new PluginsApiFactory(Path.Combine(repositoryRoot, "src", "RustServer.Plugins"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");
        response.EnsureSuccessStatusCode();

        var generatedOpenApi = await response.Content.ReadAsStringAsync();
        var generatedDocument = JsonNode.Parse(generatedOpenApi)
            ?? throw new InvalidOperationException("The OpenAPI endpoint returned an empty document.");
        var openApiPath = Path.Combine(repositoryRoot, OpenApiFileName);

        if (args.Length == 1)
        {
            if (File.Exists(openApiPath) &&
                JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(openApiPath)), generatedDocument))
            {
                Console.WriteLine("openapi.json is up to date.");
                return 0;
            }

            Console.Error.WriteLine("openapi.json is out of date. Run the API contract generator.");
            return 1;
        }

        File.WriteAllText(openApiPath, EnsureTrailingNewline(generatedOpenApi), new UTF8Encoding(false));
        Console.WriteLine($"Updated {OpenApiFileName}.");
        return 0;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startingDirectory in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "RustServer.Plugins.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Plugins repository root.");
    }

    private static void ConfigureTopologyEnvironment()
    {
        Environment.SetEnvironmentVariable("RUSTSERVER_PLUGINS_URL", "http://127.0.0.1:5114");
        Environment.SetEnvironmentVariable("RUSTSERVER_PLUGINS_BIND_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("RUSTSERVER_CONTROL_URL", "http://127.0.0.1:5112");
    }

    private static string EnsureTrailingNewline(string value) =>
        value.TrimEnd('\r', '\n') + Environment.NewLine;

    private sealed class PluginsApiFactory(string contentRoot) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot);
            builder.UseEnvironment(Environments.Production);
        }
    }
}
