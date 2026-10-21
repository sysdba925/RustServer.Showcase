using System.Globalization;

namespace RustServer.Versions.Api.Versions;

/// <summary>
/// The three-component version format used by Oxide.Rust releases.
/// </summary>
public sealed record OxideVersion(int Major, int Minor, int Patch) : IComparable<OxideVersion>
{
    public static OxideVersion FromAssemblyVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Major < 0 || version.Minor < 0 || version.Build < 0)
        {
            throw new InvalidOperationException(
                "The Oxide assembly version must contain major, minor, and patch components.");
        }

        return new OxideVersion(version.Major, version.Minor, version.Build);
    }

    public static OxideVersion Parse(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The Oxide version from {source} is missing.");
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Split('.', StringSplitOptions.None).Length != 3 ||
            !Version.TryParse(normalizedValue, out var parsedVersion) ||
            parsedVersion.Build < 0 ||
            parsedVersion.Revision >= 0)
        {
            throw new InvalidOperationException(
                $"The Oxide version from {source} must use the three-component format 'major.minor.patch'.");
        }

        return new OxideVersion(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build);
    }

    public int CompareTo(OxideVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0 ? minorComparison : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}");
}
