using System.Collections.ObjectModel;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Parses the small Updater-owned INI contract used by both the Updater and
/// Deployment Executor. Values are declarative data; variable expansion is not supported.
/// </summary>
public static class ApplicationRegistrationIniParser
{
    private static readonly string[] DefaultKeys =
    [
        "RepositoryOwner", "InstallRoot", "HealthScheme", "HealthHost", "HealthPath"
    ];

    private static readonly string[] ApplicationKeys =
    [
        "Type", "Repository", "Directory", "Service", "Entrypoint", "EntryFile", "HealthPort", "HealthPath", "InstallHook"
    ];

    /// <summary>Loads and validates registrations from an INI file.</summary>
    public static IReadOnlyCollection<ApplicationRegistration> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new ApplicationRegistryConfigurationException($"Application registry file is missing: {path}");
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (ApplicationRegistryConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ApplicationRegistryConfigurationException($"Application registry file cannot be read: {path}", exception);
        }
    }

    /// <summary>Parses and resolves all Defaults and Application sections.</summary>
    public static IReadOnlyCollection<ApplicationRegistration> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            var sections = ParseSections(content);
            if (!sections.TryGetValue("Defaults", out var defaults))
            {
                throw new ApplicationRegistrationValidationException("INI must contain one [Defaults] section.");
            }

            EnsureExactKeys(defaults, DefaultKeys, "Defaults");
            var repositoryOwner = Get(defaults, "RepositoryOwner");
            var installRoot = Get(defaults, "InstallRoot");
            var healthScheme = Get(defaults, "HealthScheme");
            var healthHost = Get(defaults, "HealthHost");
            var defaultHealthPath = Get(defaults, "HealthPath");
            ApplicationRegistrationValidation.EnsureRepositoryOwner(repositoryOwner);
            ApplicationRegistrationValidation.EnsureNormalizedLinuxAbsolutePath(installRoot, "InstallRoot");
            ApplicationRegistrationValidation.EnsureHealthScheme(healthScheme);
            ApplicationRegistrationValidation.EnsureHealthHost(healthHost);
            ApplicationRegistrationValidation.EnsureHealthPath(defaultHealthPath);

            var registrations = new List<ApplicationRegistration>();
            foreach (var (sectionName, values) in sections)
            {
                if (sectionName == "Defaults")
                {
                    continue;
                }

                if (!sectionName.StartsWith("Application:", StringComparison.Ordinal))
                {
                    throw new ApplicationRegistrationValidationException($"Unsupported INI section '{sectionName}'.");
                }

                var applicationName = sectionName["Application:".Length..];
                ApplicationRegistrationValidation.EnsureApplicationName(applicationName);
                EnsureAllowedKeys(values, ApplicationKeys, sectionName);
                var repository = Get(values, "Repository");
                var directory = Get(values, "Directory");
                var type = GetApplicationType(values);
                values.TryGetValue("Service", out var service);
                values.TryGetValue("Entrypoint", out var entrypoint);
                values.TryGetValue("EntryFile", out var entryFile);
                values.TryGetValue("InstallHook", out var installHook);
                var healthPort = Get(values, "HealthPort");
                var healthPath = values.TryGetValue("HealthPath", out var configuredHealthPath)
                    ? configuredHealthPath
                    : defaultHealthPath;
                ApplicationRegistrationValidation.EnsureRepositoryName(repository);
                ApplicationRegistrationValidation.EnsureDirectorySegment(directory);
                ApplicationRegistrationValidation.EnsureHealthPort(healthPort, out var port);
                ApplicationRegistrationValidation.EnsureHealthPath(healthPath);

                if (service is not null)
                {
                    ApplicationRegistrationValidation.EnsureServiceName(service);
                }

                if (entrypoint is not null)
                {
                    ApplicationRegistrationValidation.EnsureEntrypoint(entrypoint);
                }

                if (entryFile is not null)
                {
                    ApplicationRegistrationValidation.EnsureEntryFile(entryFile);
                }

                var installationPath = CombineLinuxPath(installRoot, directory);
                EnsurePathWithinRoot(installRoot, installationPath);
                var readinessUri = BuildReadinessUri(healthScheme, healthHost, port, healthPath);
                registrations.Add(new ApplicationRegistration(
                    applicationName,
                    $"{repositoryOwner}/{repository}",
                    installationPath,
                    type,
                    service is null ? null : $"{service}.service",
                    entrypoint,
                    entryFile,
                    new ApplicationReadinessProbe(readinessUri.AbsoluteUri),
                    installHook));
            }

            return ApplicationRegistrationValidation.ValidateCollection(registrations);
        }
        catch (ApplicationRegistrationValidationException exception)
        {
            throw new ApplicationRegistryConfigurationException(exception.Message, exception);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseSections(string content)
    {
        var sections = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        var lineNumber = 0;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var sectionName = line[1..^1];
                if (sectionName.Length == 0 || sections.ContainsKey(sectionName))
                {
                    throw new ApplicationRegistrationValidationException($"INI section is missing or duplicated at line {lineNumber}.");
                }

                current = new Dictionary<string, string>(StringComparer.Ordinal);
                sections.Add(sectionName, current);
                continue;
            }

            if (current is null)
            {
                throw new ApplicationRegistrationValidationException($"INI key appears before a section at line {lineNumber}.");
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new ApplicationRegistrationValidationException($"INI key-value pair is invalid at line {lineNumber}.");
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0 || value.Contains("${", StringComparison.Ordinal))
            {
                throw new ApplicationRegistrationValidationException($"INI value is invalid at line {lineNumber}.");
            }

            if (!current.TryAdd(key, value))
            {
                throw new ApplicationRegistrationValidationException($"INI key '{key}' is duplicated at line {lineNumber}.");
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(sections);
    }

    private static ApplicationType GetApplicationType(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("Type", out var value))
        {
            return ApplicationType.DotNet;
        }

        return value switch
        {
            "DotNet" => ApplicationType.DotNet,
            "StaticWeb" => ApplicationType.StaticWeb,
            _ => throw new ApplicationRegistrationValidationException("Application Type must be DotNet or StaticWeb.")
        };
    }

    private static void EnsureExactKeys(IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> expected, string sectionName)
    {
        var unexpected = values.Keys.Except(expected, StringComparer.Ordinal).FirstOrDefault();
        var missing = expected.Except(values.Keys, StringComparer.Ordinal).FirstOrDefault();
        if (unexpected is not null || missing is not null)
        {
            throw new ApplicationRegistrationValidationException(
                unexpected is not null
                    ? $"INI section '{sectionName}' contains unsupported key '{unexpected}'."
                    : $"INI section '{sectionName}' is missing required key '{missing}'.");
        }
    }

    private static void EnsureAllowedKeys(IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> allowed, string sectionName)
    {
        var unexpected = values.Keys.Except(allowed, StringComparer.Ordinal).FirstOrDefault();
        if (unexpected is not null)
        {
            throw new ApplicationRegistrationValidationException(
                $"INI section '{sectionName}' contains unsupported key '{unexpected}'.");
        }

        foreach (var required in new[] { "Repository", "Directory", "HealthPort" })
        {
            if (!values.ContainsKey(required))
            {
                throw new ApplicationRegistrationValidationException(
                    $"INI section '{sectionName}' is missing required key '{required}'.");
            }
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values[key];

    private static string CombineLinuxPath(string root, string directory) => root == "/" ? $"/{directory}" : $"{root}/{directory}";

    private static void EnsurePathWithinRoot(string root, string path)
    {
        if (!path.StartsWith(root + "/", StringComparison.Ordinal))
        {
            throw new ApplicationRegistrationValidationException("Resolved installation path escapes InstallRoot.");
        }
    }

    private static Uri BuildReadinessUri(string scheme, string host, int port, string path)
    {
        var builder = new UriBuilder
        {
            Scheme = scheme,
            Host = host,
            Port = port,
            Path = path
        };
        var uri = builder.Uri;
        if (!string.Equals(uri.Scheme, scheme, StringComparison.Ordinal)
            || uri.Port != port
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ApplicationRegistrationValidationException("Readiness URL could not be safely constructed.");
        }

        return uri;
    }
}
