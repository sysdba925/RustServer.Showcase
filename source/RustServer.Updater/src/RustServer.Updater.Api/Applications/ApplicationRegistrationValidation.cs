using System.Net;
using System.Text.RegularExpressions;

namespace RustServer.Updater.Api.Applications;

/// <summary>Validation shared by the INI reader and deployment-facing application model.</summary>
public static partial class ApplicationRegistrationValidation
{
    /// <summary>Validates an already resolved registration.</summary>
    public static ApplicationRegistration Validate(ApplicationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var normalized = registration with
        {
            Name = RequireValue(registration.Name, "Application name"),
            Repository = RequireValue(registration.Repository, "Repository"),
            InstallationPath = RequireValue(registration.InstallationPath, "Installation path"),
            Readiness = NormalizeReadiness(registration.Readiness)
        };

        EnsureApplicationName(normalized.Name);
        EnsureRepository(normalized.Repository);
        EnsureNormalizedLinuxAbsolutePath(normalized.InstallationPath, "Installation path");
        if (normalized.InstallHook is not null
            && normalized.InstallHook != "provisioning/provision.sh")
        {
            throw new ApplicationRegistrationValidationException("InstallHook must be the audited provisioning/provision.sh contract.");
        }

        if (normalized.Readiness is not null)
        {
            ValidateReadiness(normalized.Readiness);
        }

        return normalized.Type switch
        {
            ApplicationType.DotNet => ValidateDotNet(normalized),
            ApplicationType.StaticWeb => ValidateStaticWeb(normalized),
            _ => throw new ApplicationRegistrationValidationException("Application Type must be DotNet or StaticWeb.")
        };
    }

    /// <summary>Ensures collection-wide application, path, and unit uniqueness.</summary>
    public static IReadOnlyCollection<ApplicationRegistration> ValidateCollection(
        IEnumerable<ApplicationRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var result = registrations.Select(Validate).ToArray();
        if (result.Length == 0)
        {
            throw new ApplicationRegistrationValidationException("Application registry must contain at least one application.");
        }

        EnsureUnique(result, application => application.Name, "Application names", StringComparer.OrdinalIgnoreCase);
        EnsureUnique(result, application => application.InstallationPath, "Installation paths", StringComparer.Ordinal);
        var applicationsWithService = result
            .Where(application => application.ServiceName is not null)
            .ToArray();
        EnsureUnique(applicationsWithService, application => application.ServiceName!, "Systemd units", StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < result.Length; index++)
        {
            EnsureInstallationPathDoesNotOverlap(result[index], result.Take(index));
        }

        return result;
    }

    /// <summary>Ensures a candidate path is disjoint from all registrations except an optional name.</summary>
    public static void EnsureInstallationPathDoesNotOverlap(
        ApplicationRegistration candidate,
        IEnumerable<ApplicationRegistration> registrations,
        string? excludedName = null)
    {
        foreach (var existing in registrations)
        {
            if (excludedName is not null && string.Equals(existing.Name, excludedName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (PathsOverlap(candidate.InstallationPath, existing.InstallationPath))
            {
                throw new ApplicationRegistrationValidationException(
                    $"Installation path overlaps the registration '{existing.Name}'.");
            }
        }
    }

    internal static void EnsureApplicationName(string value)
    {
        if (!ApplicationNamePattern().IsMatch(value))
        {
            throw new ApplicationRegistrationValidationException("Application name must contain only letters, digits, dots, underscores, or hyphens.");
        }
    }

    internal static void EnsureRepositoryOwner(string value)
    {
        if (!RepositoryOwnerPattern().IsMatch(value))
        {
            throw new ApplicationRegistrationValidationException("Repository owner must be a valid GitHub owner name.");
        }
    }

    internal static void EnsureRepositoryName(string value)
    {
        if (!RepositoryNamePattern().IsMatch(value) || value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationRegistrationValidationException("Repository must be one safe repository name, without a URL or path components.");
        }
    }

    internal static void EnsureNormalizedLinuxAbsolutePath(string value, string fieldName)
    {
        if (!value.StartsWith("/", StringComparison.Ordinal)
            || value == "/"
            || value.Contains('\\')
            || value.Contains('%')
            || value.Contains("//", StringComparison.Ordinal)
            || value.EndsWith("/", StringComparison.Ordinal)
            || value.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => !PathSegmentPattern().IsMatch(segment)))
        {
            throw new ApplicationRegistrationValidationException($"{fieldName} must be a normalized absolute Linux path without encoded or traversal segments.");
        }
    }

    internal static void EnsureDirectorySegment(string value)
    {
        if (!PathSegmentPattern().IsMatch(value) || value.Contains('/') || value.Contains('\\') || value.Contains('%'))
        {
            throw new ApplicationRegistrationValidationException("Directory must be exactly one safe path segment.");
        }
    }

    internal static void EnsureServiceName(string value)
    {
        if (!SimpleNamePattern().IsMatch(value) || value.EndsWith(".service", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationRegistrationValidationException("Service must be a simple service name without the .service suffix.");
        }
    }

    internal static void EnsureEntrypoint(string value)
    {
        if (!EntrypointPattern().IsMatch(value) || value.Contains('/') || value.Contains('\\') || value.Contains('%'))
        {
            throw new ApplicationRegistrationValidationException("Entrypoint must be a simple .dll file name.");
        }
    }

    internal static void EnsureEntryFile(string value)
    {
        if (!string.Equals(value, "index.html", StringComparison.Ordinal))
        {
            throw new ApplicationRegistrationValidationException("EntryFile must be index.html.");
        }
    }

    internal static void EnsureHealthHost(string value)
    {
        if (value.Contains('%'))
        {
            throw new ApplicationRegistrationValidationException("Health host must not contain encoded or scoped-address syntax.");
        }

        if (IPAddress.TryParse(value, out _))
        {
            return;
        }

        if (value.Length > 253
            || value.EndsWith(".", StringComparison.Ordinal)
            || !value.Split('.').All(label => DnsLabelPattern().IsMatch(label)))
        {
            throw new ApplicationRegistrationValidationException("Health host must be an IP address or valid DNS name.");
        }
    }

    internal static void EnsureHealthPath(string value)
    {
        if (!value.StartsWith("/", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('%')
            || value.Contains('?')
            || value.Contains('#')
            || value.Contains("//", StringComparison.Ordinal)
            || value.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment =>
                segment is "." or ".." || !UrlPathSegmentPattern().IsMatch(segment)))
        {
            throw new ApplicationRegistrationValidationException("Health path must be an absolute URL path without query, fragment, traversal, or encoded segments.");
        }
    }

    internal static void EnsureHealthScheme(string value)
    {
        if (value is not ("http" or "https"))
        {
            throw new ApplicationRegistrationValidationException("Health scheme must be http or https.");
        }
    }

    internal static void EnsureHealthPort(string value, out int port)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)
            || port is < 1 or > 65535)
        {
            throw new ApplicationRegistrationValidationException("Health port must be an integer from 1 through 65535.");
        }
    }

    private static void EnsureRepository(string value)
    {
        var parts = value.Split('/', StringSplitOptions.None);
        if (parts.Length != 2)
        {
            throw new ApplicationRegistrationValidationException("Repository must have the form 'owner/repository'.");
        }

        EnsureRepositoryOwner(parts[0]);
        EnsureRepositoryName(parts[1]);
    }

    private static ApplicationRegistration ValidateDotNet(ApplicationRegistration registration)
    {
        EnsureAbsent(registration.EntryFile, "EntryFile is forbidden for DotNet applications.");
        var serviceName = RequireValue(registration.ServiceName, "Service name");
        var entrypoint = RequireValue(registration.Entrypoint, "Entrypoint");
        EnsureSystemdUnit(serviceName);
        EnsureEntrypoint(entrypoint);
        return registration with
        {
            ServiceName = serviceName,
            Entrypoint = entrypoint,
            EntryFile = null
        };
    }

    private static ApplicationRegistration ValidateStaticWeb(ApplicationRegistration registration)
    {
        EnsureAbsent(registration.ServiceName, "Service is forbidden for StaticWeb applications.");
        EnsureAbsent(registration.Entrypoint, "Entrypoint is forbidden for StaticWeb applications.");
        EnsureAbsent(registration.InstallHook, "InstallHook is forbidden for StaticWeb applications.");
        var entryFile = RequireValue(registration.EntryFile, "EntryFile");
        EnsureEntryFile(entryFile);
        return registration with
        {
            ServiceName = null,
            Entrypoint = null,
            EntryFile = entryFile
        };
    }

    private static void EnsureAbsent(string? value, string message)
    {
        if (value is not null)
        {
            throw new ApplicationRegistrationValidationException(message);
        }
    }

    private static void EnsureSystemdUnit(string value)
    {
        if (!ServiceNamePattern().IsMatch(value)
            || value[..(value.Length - ".service".Length)].EndsWith(".service", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationRegistrationValidationException("Service name must be a simple systemd .service unit name with one suffix.");
        }
    }

    private static void ValidateReadiness(ApplicationReadinessProbe readiness)
    {
        if (string.IsNullOrWhiteSpace(readiness.Url)
            || !Uri.TryCreate(readiness.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.HostNameType == UriHostNameType.Unknown)
        {
            throw new ApplicationRegistrationValidationException("Readiness URL must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.");
        }

        if (readiness.Attempts is < 1 or > 60
            || readiness.TimeoutSeconds is < 1 or > 60
            || readiness.DelaySeconds is < 0 or > 60)
        {
            throw new ApplicationRegistrationValidationException("Readiness attempts and timeouts are outside the supported range.");
        }
    }

    private static ApplicationReadinessProbe? NormalizeReadiness(ApplicationReadinessProbe? readiness) =>
        readiness is null ? null : readiness with { Url = RequireValue(readiness.Url, "Readiness URL") };

    private static string RequireValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ApplicationRegistrationValidationException($"{fieldName} must not be empty.");
        }

        var trimmed = value.Trim();
        if (trimmed.Contains("${", StringComparison.Ordinal))
        {
            throw new ApplicationRegistrationValidationException($"{fieldName} must not contain environment-variable substitution.");
        }

        return trimmed;
    }

    private static void EnsureUnique(
        IReadOnlyCollection<ApplicationRegistration> registrations,
        Func<ApplicationRegistration, string> selector,
        string description,
        IEqualityComparer<string> comparer)
    {
        if (registrations.Select(selector).Distinct(comparer).Count() != registrations.Count)
        {
            throw new ApplicationRegistrationValidationException($"{description} must be unique.");
        }
    }

    private static bool PathsOverlap(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal)
        || left.StartsWith(right + "/", StringComparison.Ordinal)
        || right.StartsWith(left + "/", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationNamePattern();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryOwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryNamePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\\.service$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\\.dll$", RegexOptions.CultureInvariant)]
    private static partial Regex EntrypointPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleNamePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PathSegmentPattern();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex DnsLabelPattern();

    [GeneratedRegex("^[A-Za-z0-9._~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPathSegmentPattern();
}

/// <summary>Raised when a registration is structurally invalid.</summary>
public sealed class ApplicationRegistrationValidationException(string message) : Exception(message);

/// <summary>Raised when the runtime registry cannot be read safely.</summary>
public sealed class ApplicationRegistryConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Raised when a registration cannot be found.</summary>
public sealed class ApplicationRegistrationNotFoundException(string name)
    : Exception($"Application registration '{name}' does not exist.");
