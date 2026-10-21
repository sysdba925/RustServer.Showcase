using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RustServer.Versions.ApiContractGenerator;

internal static class Program
{
    private const string OpenApiFileName = "openapi.json";
    private const string MarkdownContractRelativePath = "docs/api-contract.md";

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("Usage: dotnet run --project tools/RustServer.Versions.ApiContractGenerator -- [--verify]");
            return 0;
        }

        if (args.Length > 1 || (args.Length == 1 && args[0] != "--verify"))
        {
            Console.Error.WriteLine("Unknown argument. Use --verify to validate committed artifacts without writing them.");
            return 2;
        }

        var repositoryRoot = FindRepositoryRoot();
        ConfigureTopologyEnvironment();

        using var factory = new VersionsApiFactory(Path.Combine(repositoryRoot, "src", "RustServer.Versions.Api"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");
        response.EnsureSuccessStatusCode();

        var generatedOpenApi = await response.Content.ReadAsStringAsync();
        var openApiDocument = JsonNode.Parse(generatedOpenApi)
            ?? throw new InvalidOperationException("The OpenAPI endpoint returned an empty document.");
        var generatedMarkdown = OpenApiMarkdownRenderer.Render(openApiDocument);

        var openApiPath = Path.Combine(repositoryRoot, OpenApiFileName);
        var markdownPath = Path.Combine(repositoryRoot, MarkdownContractRelativePath);

        if (args.Length == 1)
        {
            return VerifyArtifacts(openApiPath, markdownPath, openApiDocument, generatedMarkdown) ? 0 : 1;
        }

        File.WriteAllText(openApiPath, EnsureTrailingNewline(generatedOpenApi), new UTF8Encoding(false));
        Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
        File.WriteAllText(markdownPath, generatedMarkdown, new UTF8Encoding(false));

        Console.WriteLine($"Updated {Path.GetRelativePath(repositoryRoot, openApiPath)}.");
        Console.WriteLine($"Updated {Path.GetRelativePath(repositoryRoot, markdownPath)}.");
        return 0;
    }

    private static bool VerifyArtifacts(
        string openApiPath,
        string markdownPath,
        JsonNode generatedOpenApi,
        string generatedMarkdown)
    {
        var isValid = true;

        if (!File.Exists(openApiPath) ||
            !JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(openApiPath)), generatedOpenApi))
        {
            Console.Error.WriteLine("openapi.json is out of date. Run the API contract generator.");
            isValid = false;
        }

        if (!File.Exists(markdownPath) ||
            !string.Equals(
                NormalizeNewlines(File.ReadAllText(markdownPath)),
                NormalizeNewlines(generatedMarkdown),
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine("docs/api-contract.md is out of date. Run the API contract generator.");
            isValid = false;
        }

        return isValid;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startingDirectory in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "RustServer.Versions.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Versions repository root.");
    }

    private static void ConfigureTopologyEnvironment()
    {
        Environment.SetEnvironmentVariable("RUSTSERVER_VERSIONS_URL", "http://127.0.0.1:5113");
        Environment.SetEnvironmentVariable("RUSTSERVER_VERSIONS_BIND_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("RUSTSERVER_RCON_GRPC_URL", "http://127.0.0.1:50051");
    }

    private static string EnsureTrailingNewline(string value) =>
        value.TrimEnd('\r', '\n') + Environment.NewLine;

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class VersionsApiFactory(string contentRoot) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot);
        }
    }
}
