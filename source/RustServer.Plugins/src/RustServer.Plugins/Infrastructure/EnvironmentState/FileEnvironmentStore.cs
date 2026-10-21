using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement;
using RustServer.Plugins.Features.PluginManagement.Models;
using System.Text;
using System.Text.Json;

namespace RustServer.Plugins.Infrastructure.EnvironmentState;

public sealed class FileEnvironmentStore(IOptions<PluginManagementStorageOptions> options) : IEnvironmentStore
{
    private const string StateFileName = "environment.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string stateRootPath = options.Value.StateRootPath;

    public async Task<PluginEnvironment> GetAsync(CancellationToken cancellationToken)
    {
        var stateFilePath = GetStateFilePath();

        try
        {
            var content = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
            var state = JsonSerializer.Deserialize<PersistedEnvironment>(content, SerializerOptions);

            if (!TryParseEnvironment(state?.Environment, out var environment))
            {
                throw new EnvironmentStateException(
                    "The persisted environment state contains an unsupported environment value.");
            }

            return environment;
        }
        catch (FileNotFoundException)
        {
            return PluginEnvironment.Staging;
        }
        catch (DirectoryNotFoundException)
        {
            return PluginEnvironment.Staging;
        }
        catch (JsonException exception)
        {
            throw new EnvironmentStateException("The persisted environment state is unreadable.", exception);
        }
    }

    public async Task SaveAsync(PluginEnvironment environment, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(environment))
        {
            throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported environment.");
        }

        Directory.CreateDirectory(stateRootPath);

        var stateFilePath = GetStateFilePath();
        var temporaryFilePath = Path.Combine(
            stateRootPath,
            $".{StateFileName}.{Guid.NewGuid():N}.tmp");
        var content = JsonSerializer.Serialize(
            new PersistedEnvironment(environment.ToString()),
            SerializerOptions);

        try
        {
            await File.WriteAllTextAsync(
                temporaryFilePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryFilePath, stateFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }
        }
    }

    private string GetStateFilePath() => Path.Combine(stateRootPath, StateFileName);

    private static bool TryParseEnvironment(string? value, out PluginEnvironment environment) =>
        Enum.TryParse(value, ignoreCase: true, out environment) && Enum.IsDefined(environment);

    private sealed record PersistedEnvironment(string? Environment);
}
