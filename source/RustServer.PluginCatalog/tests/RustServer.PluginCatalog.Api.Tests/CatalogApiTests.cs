using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RustServer.PluginCatalog.Api.Infrastructure.Persistence.PostgreSql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RustServer.PluginCatalog.Api.Tests;

public sealed class CatalogApiTests(CatalogApiFactory factory)
    : IClassFixture<CatalogApiFactory>
{
    [Fact]
    public async Task ListSearchesAuthorAndReturnsSources()
    {
        using var client = factory.CreateClient();
        var author = $"Unique Author {Guid.NewGuid():N}";
        using var created = await client.PostAsJsonAsync("/api/plugins", new { name = "Author Search Plugin", author });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var pluginId = await ReadIdAsync(created);
        using var source = await client.PostAsJsonAsync($"/api/plugins/{pluginId}/sources", new { url = "https://example.com/listing", label = "Listing" });
        Assert.Equal(HttpStatusCode.Created, source.StatusCode);
        using var response = await client.GetAsync($"/api/plugins?search={Uri.EscapeDataString(author.ToUpperInvariant())}&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, result.RootElement.GetProperty("totalItems").GetInt32());
        var plugin = Assert.Single(result.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(pluginId, plugin.GetProperty("id").GetGuid());
        var listedSource = Assert.Single(plugin.GetProperty("sources").EnumerateArray());
        Assert.Equal("https://example.com/listing", listedSource.GetProperty("url").GetString());
        Assert.Equal("Listing", listedSource.GetProperty("label").GetString());
    }

    [Fact]
    public async Task TagsAndFoldersOrganizePluginsAndFilterBeforePagination()
    {
        using var client = factory.CreateClient();
        using var tagResponse = await client.PostAsJsonAsync("/api/tags", new { name = "Purchased", color = "#aabbcc" });
        Assert.Equal(HttpStatusCode.Created, tagResponse.StatusCode);
        var tagId = await ReadIdAsync(tagResponse);
        using var folderResponse = await client.PostAsJsonAsync("/api/folders", new { name = "Shop" });
        Assert.Equal(HttpStatusCode.Created, folderResponse.StatusCode);
        var folderId = await ReadIdAsync(folderResponse);
        var first = await CreatePluginAsync(client);
        var second = await CreatePluginAsync(client);
        foreach (var id in new[] { first, second })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/plugins/{id}/tags/{tagId}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/plugins/{id}/folders/{folderId}", null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/plugins/{first}/tags/{tagId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tags/{tagId}", new { name = "Owned", color = "#123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/folders/{folderId}", new { name = "Stores" })).StatusCode);
        using var details = JsonDocument.Parse(await client.GetStringAsync($"/api/plugins/{first}"));
        Assert.Equal("Owned", details.RootElement.GetProperty("tags")[0].GetProperty("name").GetString());
        Assert.Equal("Stores", details.RootElement.GetProperty("folders")[0].GetProperty("name").GetString());
        using var list = JsonDocument.Parse(await client.GetStringAsync($"/api/plugins?tagId={tagId}&folderId={folderId}&pageSize=1&page=2"));
        Assert.Equal(2, list.RootElement.GetProperty("totalItems").GetInt32());
        Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("#123456", list.RootElement.GetProperty("items")[0].GetProperty("tags")[0].GetProperty("color").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/plugins/{first}/tags/{tagId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/plugins/{first}/folders/{folderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tags/{tagId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/folders/{folderId}")).StatusCode);
        using var remaining = JsonDocument.Parse(await client.GetStringAsync($"/api/plugins/{second}"));
        Assert.Empty(remaining.RootElement.GetProperty("tags").EnumerateArray());
        Assert.Empty(remaining.RootElement.GetProperty("folders").EnumerateArray());
    }

    [Fact]
    public async Task ImportPreviewReportsUnknownReferencesAndDuplicates()
    {
        using var client = factory.CreateClient();
        var name = "Import " + Guid.NewGuid().ToString("N");
        var document = new { schemaVersion = 1, plugins = new object[] { new { name, sourceDescription = "## Source", sources = new[] { new { url = "https://example.com/item", site = "New Site", label = "Listing" } }, tags = new[] { "New Tag" }, folders = new[] { "New Folder" } }, new { name, sources = Array.Empty<object>(), tags = Array.Empty<string>(), folders = Array.Empty<string>() } } };
        using var response = await client.PostAsJsonAsync("/api/import/preview", document);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var preview = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = preview.RootElement.GetProperty("entries");
        Assert.Equal("## Source", entries[0].GetProperty("input").GetProperty("sourceDescription").GetString());
        Assert.Equal("Listing", entries[0].GetProperty("input").GetProperty("sources")[0].GetProperty("label").GetString());
        Assert.Single(entries[0].GetProperty("unknownSites").EnumerateArray());
        Assert.Single(entries[0].GetProperty("unknownTags").EnumerateArray());
        Assert.Single(entries[0].GetProperty("unknownFolders").EnumerateArray());
        Assert.Single(entries[1].GetProperty("duplicates").EnumerateArray());
        using var site = await client.PostAsJsonAsync("/api/sites", new { name = "Site without URL" });
        Assert.Equal(HttpStatusCode.Created, site.StatusCode);
    }
    [Fact]
    public async Task PluginAndSourceCanBeCreatedAndRead()
    {
        using var client = factory.CreateClient();

        using var createPluginResponse = await client.PostAsJsonAsync("/api/plugins", new
        {
            name = "Example Plugin",
            slug = "Example-Plugin",
            description = "A catalog entry created through the API.",
            currentKnownVersion = "1.0.0",
            lastPublishedAt = "2026-10-16T12:00:00+00:00"
        });

        Assert.True(
            createPluginResponse.StatusCode == HttpStatusCode.Created,
            await createPluginResponse.Content.ReadAsStringAsync());
        var pluginId = await ReadIdAsync(createPluginResponse);

        using var siteResponse = await client.PostAsJsonAsync("/api/sites", new { name = "GitHub", baseUrl = "https://github.com" });
        Assert.Equal(HttpStatusCode.Created, siteResponse.StatusCode);
        var siteId = await ReadIdAsync(siteResponse);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/sites/{siteId}", new { name = "GitHub updated", baseUrl = "https://github.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/sites/{siteId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sites")).StatusCode);

        using var createSourceResponse = await client.PostAsJsonAsync($"/api/plugins/{pluginId}/sources", new
        {
            siteId,
            url = "https://github.com/example/plugin",
            label = "Repository"
        });

        Assert.Equal(HttpStatusCode.Created, createSourceResponse.StatusCode);

        using var updateResponse = await client.PutAsJsonAsync($"/api/plugins/{pluginId}", new
        {
            name = "Example Plugin",
            description = "Updated description"
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var detailsResponse = await client.GetAsync($"/api/plugins/{pluginId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStringAsync());
        var root = details.RootElement;
        Assert.Equal("example-plugin", root.GetProperty("slug").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("icon").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("iconMediaId").ValueKind);
        Assert.Equal(0, root.GetProperty("media").GetArrayLength());
        Assert.Equal(siteId, root.GetProperty("sources")[0].GetProperty("siteId").GetGuid());
        using var filtered = JsonDocument.Parse(await client.GetStringAsync($"/api/plugins?siteId={siteId}") );
        Assert.Equal(1, filtered.RootElement.GetProperty("totalItems").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/sites/{siteId}")).StatusCode);
        using var afterDelete = JsonDocument.Parse(await client.GetStringAsync($"/api/plugins/{pluginId}"));
        Assert.Equal(JsonValueKind.Null, afterDelete.RootElement.GetProperty("sources")[0].GetProperty("siteId").ValueKind);
        Assert.Equal("1.0.0", root.GetProperty("currentKnownVersion").GetString());
    }

    [Fact]
    public async Task PluginCardFieldsCanBeCreatedUpdatedAndRead()
    {
        using var client = factory.CreateClient();
        var name = $"Library {Guid.NewGuid():N}";
        using var created = await client.PostAsJsonAsync("/api/plugins", new { name });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetGuid();
        var slug = createdJson.RootElement.GetProperty("slug").GetString();

        using var duplicate = await client.PostAsJsonAsync("/api/plugins", new { name });
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.NotEqual(slug, duplicateJson.RootElement.GetProperty("slug").GetString());

        using var updated = await client.PutAsJsonAsync($"/api/plugins/{id}", new
        {
            name = name + " updated",
            author = "Author",
            description = "Brief description",
            sourceDescription = "# Source\n\nMarkdown text",
            notes = "Personal note",
            price = 12.50m,
            currency = "usd"
        });
        Assert.True(updated.StatusCode == HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        using var read = await client.GetAsync($"/api/plugins/{id}");
        using var details = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var plugin = details.RootElement;
        Assert.Equal(slug, plugin.GetProperty("slug").GetString());
        Assert.Equal("Author", plugin.GetProperty("author").GetString());
        Assert.Equal("Brief description", plugin.GetProperty("description").GetString());
        Assert.Equal("# Source\n\nMarkdown text", plugin.GetProperty("sourceDescription").GetString());
        Assert.Equal("Personal note", plugin.GetProperty("notes").GetString());
        Assert.Equal(12.50m, plugin.GetProperty("price").GetDecimal());
        Assert.Equal("USD", plugin.GetProperty("currency").GetString());

        using var list = await client.GetAsync($"/api/plugins?search={name}");
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var listed = listJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        Assert.Equal("Author", listed.GetProperty("author").GetString());
        Assert.Equal("# Source\n\nMarkdown text", listed.GetProperty("sourceDescription").GetString());
    }

    [Fact]
    public async Task TechnicalMediaCanBeCreatedAndRead()
    {
        using var client = factory.CreateClient();
        var pluginId = await CreatePluginAsync(client);

        using var response = await client.PostAsJsonAsync($"/api/plugins/{pluginId}/media/external", new
        {
            kind = "Technical",
            externalUrl = "https://example.com/technical.png",
            caption = "Configuration",
            sortOrder = 0
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        using var detailsResponse = await client.GetAsync($"/api/plugins/{pluginId}");
        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStringAsync());
        Assert.Equal("Technical", details.RootElement.GetProperty("media")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task LocalMediaCanBeUploadedReadSelectedAndDeleted()
    {
        using var client = factory.CreateClient();
        var pluginId = await CreatePluginAsync(client);

        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(CreatePngBytes());
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(image, "file", "ignored-client-name.bin");
        form.Add(new StringContent("Icon"), "kind");
        form.Add(new StringContent("Primary icon"), "caption");
        form.Add(new StringContent("0"), "sortOrder");

        using var uploadResponse = await client.PostAsync($"/api/plugins/{pluginId}/media", form);
        Assert.True(uploadResponse.IsSuccessStatusCode, await uploadResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        using var uploaded = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var mediaId = uploaded.RootElement.GetProperty("id").GetGuid();
        var accessUrl = uploaded.RootElement.GetProperty("accessUrl").GetString();
        var thumbnailAccessUrl = uploaded.RootElement.GetProperty("thumbnailAccessUrl").GetString();
        Assert.Equal("image/png", uploaded.RootElement.GetProperty("contentType").GetString());
        Assert.NotNull(accessUrl);
        Assert.NotNull(thumbnailAccessUrl);

        using var contentResponse = await client.GetAsync(accessUrl);
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("image/png", contentResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", contentResponse.Headers.CacheControl?.ToString());

        using var thumbnailResponse = await client.GetAsync(thumbnailAccessUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.Equal("image/webp", thumbnailResponse.Content.Headers.ContentType?.MediaType);

        using var setIconResponse = await client.PutAsJsonAsync($"/api/plugins/{pluginId}/icon", new { mediaId });
        Assert.Equal(HttpStatusCode.OK, setIconResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listedPlugin = list.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == pluginId);
        Assert.Equal(mediaId, listedPlugin.GetProperty("icon").GetProperty("id").GetGuid());

        using var detailsResponse = await client.GetAsync($"/api/plugins/{pluginId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStringAsync());
        Assert.Equal(mediaId, details.RootElement.GetProperty("iconMediaId").GetGuid());
        Assert.Single(details.RootElement.GetProperty("media").EnumerateArray());

        using var deleteResponse = await client.DeleteAsync($"/api/plugins/{pluginId}/media/{mediaId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var detailsAfterDeleteResponse = await client.GetAsync($"/api/plugins/{pluginId}");
        using var detailsAfterDelete = JsonDocument.Parse(await detailsAfterDeleteResponse.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, detailsAfterDelete.RootElement.GetProperty("iconMediaId").ValueKind);
        Assert.Equal(0, detailsAfterDelete.RootElement.GetProperty("media").GetArrayLength());
    }

    private static async Task<Guid> CreatePluginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/plugins", new
        {
            name = "Media Example Plugin",
            slug = $"media-example-{Guid.NewGuid():N}",
            description = "A catalog entry used by the media API smoke test."
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    private static byte[] CreatePngBytes()
    {
        using var image = new MagickImage(MagickColors.DodgerBlue, 1, 1);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }
}

public sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"catalog-api-{Guid.NewGuid():N}";
    private readonly string mediaRoot = Path.Combine(Path.GetTempPath(), $"plugin-catalog-media-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(mediaRoot);

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaStorage:Provider"] = "Local",
                ["MediaStorage:RootPath"] = mediaRoot
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PluginCatalogDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<PluginCatalogDbContext>>();
            services.RemoveAll<PluginCatalogDbContext>();
            services.AddDbContext<PluginCatalogDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(mediaRoot))
        {
            Directory.Delete(mediaRoot, recursive: true);
        }
    }
}
