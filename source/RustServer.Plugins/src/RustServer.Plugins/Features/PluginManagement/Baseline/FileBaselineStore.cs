using Microsoft.Extensions.Options;
using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using System.Text;
using System.Text.Json;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.Baseline;

public sealed class FileBaselineStore(IOptions<PluginManagementStorageOptions> options) : IBaselineStore
{
    private const string StateFileName = "baseline.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string stateRootPath = options.Value.StateRootPath;

    public async Task<BaselineModel?> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(GetStateFilePath(), cancellationToken);
            var persistedBaseline = JsonSerializer.Deserialize<PersistedBaseline>(content, SerializerOptions);

            return ToBaseline(persistedBaseline);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new BaselineStateException("The persisted baseline state is unreadable.", exception);
        }
        catch (IOException exception)
        {
            throw new BaselineStateException("The persisted baseline state is unreadable.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new BaselineStateException("The persisted baseline state is unreadable.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new BaselineStateException("The persisted baseline state is invalid.", exception);
        }
    }

    public async Task SaveAsync(BaselineModel baseline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        Directory.CreateDirectory(stateRootPath);

        var stateFilePath = GetStateFilePath();
        var temporaryFilePath = Path.Combine(
            stateRootPath,
            $".{StateFileName}.{Guid.NewGuid():N}.tmp");
        var content = JsonSerializer.Serialize(
            new PersistedBaseline(
                baseline.Environment.ToString(),
                baseline.Source.Kind.ToString(),
                baseline.Release?.Version,
                baseline.PluginsCommit.Value,
                baseline.ConfigsCommit.Value,
                baseline.AppliedAt),
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

    private static BaselineModel ToBaseline(PersistedBaseline? persistedBaseline)
    {
        if (persistedBaseline is null ||
            !TryParseEnvironment(persistedBaseline.Environment, out var environment) ||
            persistedBaseline.AppliedAt is not { } appliedAt ||
            appliedAt == default)
        {
            throw new ArgumentException("The persisted baseline state is incomplete.");
        }

        var source = ToSource(persistedBaseline.Source, persistedBaseline.Release);

        return new BaselineModel(
            environment,
            source,
            new GitCommitSha(persistedBaseline.PluginsCommit ?? string.Empty),
            new GitCommitSha(persistedBaseline.ConfigsCommit ?? string.Empty),
            appliedAt);
    }

    private static bool TryParseEnvironment(string? value, out PluginEnvironment environment) =>
        Enum.TryParse(value, ignoreCase: true, out environment) && Enum.IsDefined(environment);

    private static Source ToSource(string? sourceValue, string? releaseValue)
    {
        if (!Enum.TryParse<SourceKind>(sourceValue, ignoreCase: true, out var sourceKind) ||
            !Enum.IsDefined(sourceKind))
        {
            throw new ArgumentException("The persisted baseline state contains an unsupported source value.");
        }

        return sourceKind switch
        {
            SourceKind.Development when releaseValue is null => Source.Development,
            SourceKind.Release when !string.IsNullOrWhiteSpace(releaseValue) =>
                Source.ForRelease(new Release(releaseValue)),
            _ => throw new ArgumentException("The persisted baseline state contains an inconsistent release value.")
        };
    }

    private sealed record PersistedBaseline(
        string? Environment,
        string? Source,
        string? Release,
        string? PluginsCommit,
        string? ConfigsCommit,
        DateTimeOffset? AppliedAt);
}
