using System.Reflection;

namespace RustServer.Versions.Api.Versions;

/// <summary>
/// Reads the installed Oxide version from assembly metadata without loading the assembly.
/// </summary>
public sealed class OxideAssemblyInstalledVersionReader : IOxideInstalledVersionReader
{
    public OxideVersion? ReadVersion(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new InvalidOperationException("OxideVersions:AssemblyPath must be configured.");
        }

        if (!File.Exists(assemblyPath))
        {
            // Oxide has not been installed yet; this is an expected first-run state.
            return null;
        }

        try
        {
            var assemblyVersion = AssemblyName.GetAssemblyName(assemblyPath).Version
                ?? throw new InvalidOperationException("The Oxide assembly does not contain version metadata.");

            return OxideVersion.FromAssemblyVersion(assemblyVersion);
        }
        catch (Exception exception) when (exception is BadImageFormatException
                                          or FileLoadException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The configured Oxide assembly metadata could not be read.",
                exception);
        }
    }
}
