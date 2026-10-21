using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace RustServer.Plugins.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task CommittedOpenApi_MatchesRunningProductionApi()
    {
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");

        response.EnsureSuccessStatusCode();

        var generatedOpenApi = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotNull(generatedOpenApi);

        var committedOpenApi = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "openapi.json")));
        Assert.True(
            JsonNode.DeepEquals(committedOpenApi, generatedOpenApi),
            "openapi.json is out of date. Run the API contract generator.");
    }

    [Fact]
    public async Task Document_ContainsOnlyThePublishedEndpointSetAndStringWireSchemas()
    {
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi.json");

        response.EnsureSuccessStatusCode();

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var paths = document["paths"]!.AsObject();
        var actualEndpoints = paths
            .SelectMany(path => path.Value!.AsObject().Select(operation => $"{operation.Key.ToUpperInvariant()} {path.Key}"))
            .ToHashSet(StringComparer.Ordinal);

        var expectedEndpoints = new HashSet<string>(StringComparer.Ordinal)
        {
            "GET /health",
            "GET /api/environment",
            "PUT /api/environment",
            "POST /api/git/fetch",
            "POST /api/git/sync",
            "GET /api/git/status",
            "GET /api/releases",
            "GET /api/baseline",
            "GET /api/runtime/diff",
            "GET /api/runtime/diff/content",
            "GET /api/runtime/events",
            "POST /api/source/preview",
            "POST /api/source/apply",
            "POST /api/runtime/commit",
            "POST /api/runtime/revert",
            "GET /api/operations",
            "GET /api/operations/{operationId}"
        };

        Assert.True(expectedEndpoints.SetEquals(actualEndpoints));

        AssertResponseCodes(paths, "/api/git/fetch", "post", "202", "409", "500", "502", "503");
        AssertResponseCodes(paths, "/api/git/sync", "post", "202", "400", "409", "500", "502", "503");
        AssertResponseCodes(paths, "/api/source/apply", "post", "202", "400", "409", "500", "502", "503");
        AssertResponseCodes(paths, "/api/runtime/commit", "post", "202", "400", "409", "500", "502", "503");
        AssertResponseCodes(paths, "/api/runtime/revert", "post", "202", "400", "409", "500", "503");
        AssertResponseCodes(paths, "/api/runtime/diff/content", "get", "200", "400", "404", "500", "503");
        AssertResponseCodes(paths, "/api/runtime/events", "get", "200");
        AssertResponseCodes(paths, "/api/operations", "get", "200", "400", "503");
        AssertResponseCodes(paths, "/api/operations/{operationId}", "get", "200", "404", "503");
        Assert.NotNull(paths["/api/runtime/events"]!["get"]!["responses"]!["200"]!["content"]!["text/event-stream"]);

        var operationHistoryLimit = paths["/api/operations"]!["get"]!["parameters"]!
            .AsArray()
            .Single(parameter => string.Equals(parameter!["name"]?.GetValue<string>(), "limit", StringComparison.Ordinal))!["schema"]!
            .AsObject();
        Assert.Equal(1, operationHistoryLimit["minimum"]?.GetValue<int>());
        Assert.Equal(200, operationHistoryLimit["maximum"]?.GetValue<int>());
        Assert.Equal(20, operationHistoryLimit["default"]?.GetValue<int>());

        var schemas = document["components"]!["schemas"]!.AsObject();
        AssertStringSchema(schemas, "GetEnvironmentResponse", "environment", "systemState");
        AssertStringSchema(schemas, "TargetSourceRequest", "source");
        AssertStringSchema(schemas, "RuntimeDiffChangeResponse", "change");
        AssertStringSchema(schemas, "RuntimeDiffContentResponse", "repository", "path", "change");
        AssertStringSchema(schemas, "RuntimeDiffContentSideResponse", "kind");
        AssertStringSchema(schemas, "GetOperationResponse", "status");
        AssertStringSchema(schemas, "OperationHistoryItemResponse", "operationId", "type", "status");
        AssertStringSchema(schemas, "OperationStepResponse", "status");
        AssertStringSchema(schemas, "OperationRollbackResponse", "status");

        var error = schemas["ApiErrorResponse"]!.AsObject();
        AssertStringSchema(schemas, "ApiErrorResponse", "code", "message");
        Assert.Equal("object", error["properties"]!["details"]!["type"]?.GetValue<string>());
    }

    private static void AssertStringSchema(JsonObject schemas, string schemaName, params string[] properties)
    {
        var schemaProperties = schemas[schemaName]!["properties"]!.AsObject();
        foreach (var property in properties)
        {
            Assert.Equal("string", schemaProperties[property]!["type"]?.GetValue<string>());
        }
    }

    private static void AssertResponseCodes(
        JsonObject paths,
        string path,
        string method,
        params string[] expectedStatusCodes)
    {
        var actualStatusCodes = paths[path]![method]!["responses"]!
            .AsObject()
            .Select(response => response.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(actualStatusCodes.SetEquals(expectedStatusCodes));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RustServer.Plugins.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RustServer.Plugins repository root.");
    }

    private sealed class ProductionApiFactory : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(Environments.Production);
    }
}
