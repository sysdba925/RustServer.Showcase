using System.Text.RegularExpressions;
using RustServer.Control.Application.Wipe;
using RustServer.Control.Domain.Wipe;
using RustServer.Control.Infrastructure.Servers;

namespace RustServer.Control.Infrastructure.Wipe;

/// <summary>
/// Deletes only explicitly recognized Rust state files. It never recursively deletes a directory,
/// and it does not enumerate or alter Oxide plugins, configuration, or unregistered plugin data.
/// </summary>
public sealed partial class WipeEngine : IWipeEngine
{
    private readonly RustServerControlOptions _options;
    private readonly IReadOnlyDictionary<string, IPluginDataWipeHandler> _pluginDataHandlers;

    public WipeEngine(
        RustServerControlOptions options,
        IEnumerable<IPluginDataWipeHandler> pluginDataHandlers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pluginDataHandlers);

        _options = options;
        _pluginDataHandlers = pluginDataHandlers.ToDictionary(
            handler => handler.Target,
            StringComparer.Ordinal);
    }

    public string? ValidatePlan(WipePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.World is not (WipeAction.Keep or WipeAction.Delete))
        {
            return "World supports only keep or delete.";
        }

        if (plan.Blueprints is not (WipeAction.Keep or WipeAction.Delete))
        {
            return "Blueprints supports only keep or delete.";
        }

        var pluginTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pluginDataAction in plan.PluginData)
        {
            if (string.IsNullOrWhiteSpace(pluginDataAction.Target))
            {
                return "A plugin-data target name is required.";
            }

            if (!pluginTargets.Add(pluginDataAction.Target))
            {
                return $"Plugin-data target '{pluginDataAction.Target}' appears more than once.";
            }

            if (!_pluginDataHandlers.ContainsKey(pluginDataAction.Target))
            {
                return $"Plugin-data target '{pluginDataAction.Target}' is not registered.";
            }

            if (!Enum.IsDefined(pluginDataAction.Action))
            {
                return $"Plugin-data target '{pluginDataAction.Target}' has an invalid action.";
            }
        }

        return null;
    }

    public async Task<WipeExecutionReport> ExecuteAsync(
        WipePlan plan,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidatePlan(plan);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError, nameof(plan));
        }

        var paths = ResolvePaths();
        var results = new List<WipeTargetResult>();

        if (!await ExecuteCoreTargetAsync(
            "world",
            plan.World,
            paths.IdentityDirectory,
            IsWorldFile,
            results,
            cancellationToken))
        {
            AddNotExecutedTargets(plan, results, startAtPluginTarget: null);
            return new WipeExecutionReport(false, results);
        }

        if (!await ExecuteCoreTargetAsync(
            "blueprints",
            plan.Blueprints,
            paths.IdentityDirectory,
            IsBlueprintFile,
            results,
            cancellationToken))
        {
            AddNotExecutedTargets(plan, results, startAtPluginTarget: null);
            return new WipeExecutionReport(false, results);
        }

        foreach (var pluginDataAction in plan.PluginData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pluginDataAction.Action == WipeAction.Keep)
            {
                results.Add(new WipeTargetResult(
                    pluginDataAction.Target,
                    pluginDataAction.Action,
                    WipeTargetOutcome.Kept,
                    0,
                    null));
                continue;
            }

            try
            {
                var handler = _pluginDataHandlers[pluginDataAction.Target];
                var handlerResult = await handler.ExecuteAsync(
                    pluginDataAction.Action,
                    new WipePluginDataContext(paths.PluginDataDirectory),
                    cancellationToken);
                results.Add(new WipeTargetResult(
                    pluginDataAction.Target,
                    pluginDataAction.Action,
                    pluginDataAction.Action == WipeAction.Delete
                        ? WipeTargetOutcome.Deleted
                        : WipeTargetOutcome.Transformed,
                    handlerResult.AffectedItems,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                results.Add(new WipeTargetResult(
                    pluginDataAction.Target,
                    pluginDataAction.Action,
                    WipeTargetOutcome.Failed,
                    0,
                    "The target could not be processed."));
                AddNotExecutedTargets(plan, results, pluginDataAction.Target);
                return new WipeExecutionReport(false, results);
            }
        }

        return new WipeExecutionReport(true, results);
    }

    private static async Task<bool> ExecuteCoreTargetAsync(
        string target,
        WipeAction action,
        string identityDirectory,
        Func<string, bool> matches,
        ICollection<WipeTargetResult> results,
        CancellationToken cancellationToken)
    {
        if (action == WipeAction.Keep)
        {
            results.Add(new WipeTargetResult(target, action, WipeTargetOutcome.Kept, 0, null));
            return true;
        }

        try
        {
            var deletedItems = 0;
            if (Directory.Exists(identityDirectory))
            {
                foreach (var candidatePath in Directory.EnumerateFiles(identityDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!matches(Path.GetFileName(candidatePath)))
                    {
                        continue;
                    }

                    EnsureContainedBy(candidatePath, identityDirectory);
                    var attributes = File.GetAttributes(candidatePath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    File.Delete(candidatePath);
                    deletedItems++;
                }
            }

            results.Add(new WipeTargetResult(target, action, WipeTargetOutcome.Deleted, deletedItems, null));
            await Task.CompletedTask;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            results.Add(new WipeTargetResult(
                target,
                action,
                WipeTargetOutcome.Failed,
                0,
                "The target could not be processed."));
            return false;
        }
    }

    private ResolvedPaths ResolvePaths()
    {
        if (string.IsNullOrWhiteSpace(_options.RustServerDataPath))
        {
            throw new WipeConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:RustServerDataPath' is required for wipe operations.");
        }

        if (string.IsNullOrWhiteSpace(_options.RustServerIdentity)
            || !IdentityNameExpression().IsMatch(_options.RustServerIdentity))
        {
            throw new WipeConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:RustServerIdentity' must be a simple server identity.");
        }

        var root = Path.GetFullPath(_options.RustServerDataPath);
        if (!Path.IsPathFullyQualified(root) || IsFilesystemRoot(root))
        {
            throw new WipeConfigurationException(
                $"Configuration value '{RustServerControlOptions.SectionName}:RustServerDataPath' must be a non-root absolute directory.");
        }

        var serverDirectory = CombineContained(root, "server");
        var identityDirectory = CombineContained(serverDirectory, _options.RustServerIdentity);
        var oxideDirectory = CombineContained(root, "oxide");
        var pluginDataDirectory = CombineContained(oxideDirectory, "data");

        EnsureExistingDirectoryIsNotReparsePoint(serverDirectory);
        EnsureExistingDirectoryIsNotReparsePoint(identityDirectory);
        EnsureExistingDirectoryIsNotReparsePoint(oxideDirectory);
        EnsureExistingDirectoryIsNotReparsePoint(pluginDataDirectory);

        return new ResolvedPaths(identityDirectory, pluginDataDirectory);
    }

    private static void AddNotExecutedTargets(
        WipePlan plan,
        ICollection<WipeTargetResult> results,
        string? startAtPluginTarget)
    {
        if (!results.Any(result => result.Target == "world"))
        {
            results.Add(new WipeTargetResult("world", plan.World, WipeTargetOutcome.NotExecuted, 0, null));
        }

        if (!results.Any(result => result.Target == "blueprints"))
        {
            results.Add(new WipeTargetResult("blueprints", plan.Blueprints, WipeTargetOutcome.NotExecuted, 0, null));
        }

        var add = startAtPluginTarget is null;
        foreach (var pluginDataAction in plan.PluginData)
        {
            if (pluginDataAction.Target == startAtPluginTarget)
            {
                add = true;
                continue;
            }

            if (add && !results.Any(result => result.Target == pluginDataAction.Target))
            {
                results.Add(new WipeTargetResult(
                    pluginDataAction.Target,
                    pluginDataAction.Action,
                    WipeTargetOutcome.NotExecuted,
                    0,
                    null));
            }
        }
    }

    private static string CombineContained(string parent, string child)
    {
        var result = Path.GetFullPath(Path.Combine(parent, child));
        EnsureContainedBy(result, parent);
        return result;
    }

    private static void EnsureContainedBy(string candidate, string parent)
    {
        var fullParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new WipeConfigurationException("A wipe target resolved outside the configured Rust server data directory.");
        }
    }

    private static bool IsFilesystemRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root)
            && string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureExistingDirectoryIsNotReparsePoint(string path)
    {
        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new WipeConfigurationException(
                "A configured wipe directory must not be a filesystem reparse point.");
        }
    }

    private static bool IsWorldFile(string fileName) =>
        fileName.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
        || WorldSaveFileExpression().IsMatch(fileName);

    private static bool IsBlueprintFile(string fileName) => BlueprintFileExpression().IsMatch(fileName);

    [GeneratedRegex(@"\.sav(?:\.\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorldSaveFileExpression();

    [GeneratedRegex(@"^player\.blueprints(?:\.\d+)?(?:\.db)?(?:-(?:wal|shm|journal))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlueprintFileExpression();

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityNameExpression();

    private sealed record ResolvedPaths(string IdentityDirectory, string PluginDataDirectory);
}
