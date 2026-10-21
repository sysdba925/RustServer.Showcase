using RustServer.Versions.Api.Versions;

namespace RustServer.Versions.Tests;

public sealed class SteamAppManifestBuildIdReaderTests
{
    [Fact]
    public void ReadBuildId_ReadsBuildIdFromAppState()
    {
        var manifestPath = CreateManifest(
            """
            "AppState"
            {
                "appid"       "258550"
                "name"        "Rust Dedicated Server"
                "UserConfig"
                {
                    "buildid" "99999999"
                }
                "buildid"     "12345678"
            }
            """);

        try
        {
            var reader = new SteamAppManifestBuildIdReader();

            var buildId = reader.ReadBuildId(manifestPath);

            Assert.Equal(12345678UL, buildId);
        }
        finally
        {
            DeleteManifestDirectory(manifestPath);
        }
    }

    [Fact]
    public void ReadBuildId_ThrowsWhenBuildIdIsMissing()
    {
        var manifestPath = CreateManifest(
            """
            "AppState"
            {
                "appid" "258550"
            }
            """);

        try
        {
            var reader = new SteamAppManifestBuildIdReader();

            Assert.Throws<InvalidOperationException>(() => reader.ReadBuildId(manifestPath));
        }
        finally
        {
            DeleteManifestDirectory(manifestPath);
        }
    }

    [Fact]
    public void ReadBuildId_ThrowsWhenBuildIdIsInvalid()
    {
        var manifestPath = CreateManifest(
            """
            "AppState"
            {
                "appid" "258550"
                "buildid" "not-a-build-id"
            }
            """);

        try
        {
            var reader = new SteamAppManifestBuildIdReader();

            Assert.Throws<InvalidOperationException>(() => reader.ReadBuildId(manifestPath));
        }
        finally
        {
            DeleteManifestDirectory(manifestPath);
        }
    }

    [Fact]
    public void ReadBuildId_ThrowsWhenManifestBelongsToAnotherApp()
    {
        var manifestPath = CreateManifest(
            """
            "AppState"
            {
                "appid" "440"
                "buildid" "12345678"
            }
            """);

        try
        {
            var reader = new SteamAppManifestBuildIdReader();

            Assert.Throws<InvalidOperationException>(() => reader.ReadBuildId(manifestPath));
        }
        finally
        {
            DeleteManifestDirectory(manifestPath);
        }
    }

    [Fact]
    public void ReadBuildId_ReturnsNullWhenManifestDoesNotExist()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "appmanifest_258550.acf");

        Assert.Null(new SteamAppManifestBuildIdReader().ReadBuildId(missingPath));
    }

    private static string CreateManifest(string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RustServer.Versions.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "appmanifest_258550.acf");
        File.WriteAllText(manifestPath, contents);
        return manifestPath;
    }

    private static void DeleteManifestDirectory(string manifestPath)
    {
        Directory.Delete(Path.GetDirectoryName(manifestPath)!, recursive: true);
    }
}
