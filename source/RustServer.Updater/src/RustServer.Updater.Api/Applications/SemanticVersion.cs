using System.Globalization;
using System.Numerics;

namespace RustServer.Updater.Api.Applications;

/// <summary>
/// Parses and compares Semantic Versioning 2.0.0 release tags, including an optional leading <c>v</c>.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private readonly IReadOnlyList<string> _prereleaseIdentifiers;

    private SemanticVersion(BigInteger major, BigInteger minor, BigInteger patch, IReadOnlyList<string> prereleaseIdentifiers)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prereleaseIdentifiers = prereleaseIdentifiers;
    }

    /// <summary>Gets the major component.</summary>
    public BigInteger Major { get; }

    /// <summary>Gets the minor component.</summary>
    public BigInteger Minor { get; }

    /// <summary>Gets the patch component.</summary>
    public BigInteger Patch { get; }

    /// <summary>Gets the prerelease identifiers.</summary>
    public IReadOnlyList<string> PrereleaseIdentifiers => _prereleaseIdentifiers;

    /// <summary>
    /// Attempts to parse a Semantic Versioning 2.0.0 tag.
    /// </summary>
    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tag = value[0] is 'v' or 'V' ? value[1..] : value;
        var buildSeparator = tag.IndexOf('+');
        var versionAndPrerelease = buildSeparator >= 0 ? tag[..buildSeparator] : tag;
        var buildMetadata = buildSeparator >= 0 ? tag[(buildSeparator + 1)..] : null;

        if (buildMetadata is not null && !AreValidIdentifiers(buildMetadata, numericIdentifiersMayHaveLeadingZeros: true))
        {
            return false;
        }

        var prereleaseSeparator = versionAndPrerelease.IndexOf('-');
        var core = prereleaseSeparator >= 0 ? versionAndPrerelease[..prereleaseSeparator] : versionAndPrerelease;
        var prerelease = prereleaseSeparator >= 0 ? versionAndPrerelease[(prereleaseSeparator + 1)..] : null;
        var coreParts = core.Split('.');

        if (coreParts.Length != 3
            || !TryParseCorePart(coreParts[0], out var major)
            || !TryParseCorePart(coreParts[1], out var minor)
            || !TryParseCorePart(coreParts[2], out var patch)
            || (prerelease is not null && !AreValidIdentifiers(prerelease, numericIdentifiersMayHaveLeadingZeros: false)))
        {
            return false;
        }

        version = new SemanticVersion(
            major,
            minor,
            patch,
            prerelease is null ? [] : prerelease.Split('.'));
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison = Major.CompareTo(other.Major);
        coreComparison = coreComparison != 0 ? coreComparison : Minor.CompareTo(other.Minor);
        coreComparison = coreComparison != 0 ? coreComparison : Patch.CompareTo(other.Patch);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (_prereleaseIdentifiers.Count == 0 || other._prereleaseIdentifiers.Count == 0)
        {
            return _prereleaseIdentifiers.Count == other._prereleaseIdentifiers.Count
                ? 0
                : _prereleaseIdentifiers.Count == 0 ? 1 : -1;
        }

        for (var index = 0; index < Math.Min(_prereleaseIdentifiers.Count, other._prereleaseIdentifiers.Count); index++)
        {
            var comparison = ComparePrereleaseIdentifier(_prereleaseIdentifiers[index], other._prereleaseIdentifiers[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return _prereleaseIdentifiers.Count.CompareTo(other._prereleaseIdentifiers.Count);
    }

    private static bool TryParseCorePart(string part, out BigInteger value)
    {
        value = BigInteger.Zero;
        return !string.IsNullOrEmpty(part)
               && (part.Length == 1 || part[0] != '0')
               && BigInteger.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool AreValidIdentifiers(string identifiers, bool numericIdentifiersMayHaveLeadingZeros)
    {
        var parts = identifiers.Split('.');
        return parts.Length > 0 && parts.All(part =>
            part.Length > 0
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && (numericIdentifiersMayHaveLeadingZeros
                || !part.All(char.IsAsciiDigit)
                || part.Length == 1
                || part[0] != '0'));
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftIsNumeric = left.All(char.IsAsciiDigit);
        var rightIsNumeric = right.All(char.IsAsciiDigit);
        if (leftIsNumeric && rightIsNumeric)
        {
            return CompareNumericIdentifiers(left, right);
        }

        if (leftIsNumeric != rightIsNumeric)
        {
            return leftIsNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static int CompareNumericIdentifiers(string left, string right) =>
        left.Length != right.Length
            ? left.Length.CompareTo(right.Length)
            : string.CompareOrdinal(left, right);
}
