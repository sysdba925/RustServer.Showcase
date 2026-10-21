using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RustServer.Auth.Tests.Integration;

public sealed class ProductionStartupTests
{
    [Fact]
    public async Task PublishedApplication_LoadsProductionConfigurationOutsideItsWorkingDirectory()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "RustServer.Auth.Tests",
            Guid.NewGuid().ToString("N"));
        string applicationDirectory = Path.Combine(testDirectory, "application");
        string workingDirectory = Path.Combine(testDirectory, "working");
        Process? process = null;
        Directory.CreateDirectory(applicationDirectory);
        Directory.CreateDirectory(workingDirectory);

        try
        {
            CopyApplicationFiles(applicationDirectory);

            int port = GetAvailablePort();
            string databasePath = Path.Combine(testDirectory, "auth.db");
            await File.WriteAllTextAsync(
                Path.Combine(applicationDirectory, "appsettings.Production.json"),
                JsonSerializer.Serialize(new
                {
                    ConnectionStrings = new
                    {
                        AuthDatabase = $"Data Source={databasePath}"
                    },
                    Kestrel = new
                    {
                        Endpoints = new
                        {
                            Http = new
                            {
                                Url = $"http://127.0.0.1:{port}",
                                Protocols = "Http1"
                            }
                        }
                    }
                }));

            process = StartApplication(applicationDirectory, workingDirectory);
            await AssertApplicationIsReadyAsync(process, port);
        }
        finally
        {
            process?.Dispose();

            if (Directory.Exists(testDirectory))
            {
                await DeleteDirectoryAsync(testDirectory);
            }
        }
    }

    private static Process StartApplication(
        string applicationDirectory,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(Path.Combine(applicationDirectory, "RustServer.Auth.dll"));
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ADMIN_USERNAME"] = "admin";
        startInfo.Environment["ADMIN_PASSWORD"] = "BootstrapPassword!123";
        startInfo.Environment["GOOGLE_CLIENT_ID"] = "test-client-id";
        startInfo.Environment["GOOGLE_CLIENT_SECRET"] = "test-client-secret";
        startInfo.Environment.Remove("ASPNETCORE_URLS");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the published Auth application.");
    }

    private static async Task AssertApplicationIsReadyAsync(Process process, int port)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(1)
        };
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        Uri readinessEndpoint = new($"http://127.0.0.1:{port}/health/ready");
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(readinessEndpoint);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException)
                {
                }

                await Task.Delay(200);
            }

            throw new Xunit.Sdk.XunitException(
                $"Published Auth application did not become ready at {readinessEndpoint}.{Environment.NewLine}" +
                $"Standard output:{Environment.NewLine}{await standardOutput}{Environment.NewLine}" +
                $"Standard error:{Environment.NewLine}{await standardError}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
        }
    }

    private static void CopyApplicationFiles(string destinationDirectory)
    {
        foreach (string sourceFile in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile);
        }

        CopyDirectory(
            Path.Combine(AppContext.BaseDirectory, "runtimes"),
            Path.Combine(destinationDirectory, "runtimes"));
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(sourceFile, Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)));
        }

        foreach (string sourceSubdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                sourceSubdirectory,
                Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory)));
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task DeleteDirectoryAsync(string directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(200);
            }
        }
    }
}
