namespace RustServer.Control.Infrastructure.Servers;

public sealed class OxideUpdateOptions
{
    public const string SectionName = "OxideUpdate";

    public string OxideComposePath { get; init; } = string.Empty;
}
