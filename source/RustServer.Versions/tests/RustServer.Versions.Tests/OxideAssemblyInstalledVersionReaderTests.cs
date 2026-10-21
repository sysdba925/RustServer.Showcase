using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Tests;

public sealed class OxideAssemblyInstalledVersionReaderTests
{
    [Fact]
    public void ReadVersion_ReadsAndNormalizesVersionFromManagedAssembly()
    {
        var reader = new OxideAssemblyInstalledVersionReader();

        var version = reader.ReadVersion(typeof(OxideAssemblyInstalledVersionReaderTests).Assembly.Location);

        Assert.Equal("2.0.7726", version?.ToString());
    }

    [Fact]
    public void ReadVersion_ThrowsClearlyWhenAssemblyPathIsNotConfigured()
    {
        var reader = new OxideAssemblyInstalledVersionReader();

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadVersion(" "));

        Assert.Contains("OxideVersions:AssemblyPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadVersion_ReturnsNullWhenAssemblyIsMissing()
    {
        var reader = new OxideAssemblyInstalledVersionReader();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Oxide.Rust.dll");

        Assert.Null(reader.ReadVersion(missingPath));
    }

    [Fact]
    public void ReadVersion_ThrowsClearlyWhenAssemblyIsNotManaged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RustServer.Versions.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "Oxide.Rust.dll");
        File.WriteAllText(assemblyPath, "not a managed assembly");

        try
        {
            var reader = new OxideAssemblyInstalledVersionReader();

            var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadVersion(assemblyPath));

            Assert.Contains("metadata could not be read", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
