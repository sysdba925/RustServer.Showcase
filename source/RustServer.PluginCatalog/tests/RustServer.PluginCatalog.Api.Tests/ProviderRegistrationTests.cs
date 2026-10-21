using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RustServer.PluginCatalog.Api.CompositionRoot;

namespace RustServer.PluginCatalog.Api.Tests;

public sealed class ProviderRegistrationTests
{
    [Fact]
    public void UnsupportedDatabaseProviderFailsWithoutFallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["MediaStorage:Provider"] = "Local"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPluginCatalogInfrastructure(configuration));

        Assert.Contains("Database provider 'SqlServer' is not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedMediaProviderFailsWithoutFallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["MediaStorage:Provider"] = "S3"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPluginCatalogInfrastructure(configuration));

        Assert.Contains("Media storage provider 'S3' is not supported", exception.Message, StringComparison.Ordinal);
    }
}
