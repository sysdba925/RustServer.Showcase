using Microsoft.Extensions.Options;

namespace RustServer.Updater.Api.Applications;

/// <summary>Configures the installation-wide release channel file.</summary>
public sealed class ReleaseChannelOptions
{
    public const string SectionName = "ReleaseChannel";

    /// <summary>Gets or sets the durable installation-wide release-channel path.</summary>
    public string StoragePath { get; init; } = "/etc/rustserver/updater-release-channel";
}

/// <summary>Gets the one release channel shared by every registered application.</summary>
public interface IReleaseChannelProvider
{
    ReleaseChannel GetChannel();
}

/// <summary>Loads and validates the release channel kept outside the mutable release directory.</summary>
public sealed class FileReleaseChannelProvider : IReleaseChannelProvider
{
    private readonly ReleaseChannel _channel;

    public FileReleaseChannelProvider(IOptions<ReleaseChannelOptions> options)
    {
        var path = options.Value.StoragePath;
        if (string.IsNullOrWhiteSpace(path)
            || (!path.StartsWith("/", StringComparison.Ordinal) && !Path.IsPathFullyQualified(path)))
        {
            throw new InvalidOperationException("Release channel storage path must be absolute.");
        }

        try
        {
            var value = File.ReadAllText(path).Trim();
            if (!Enum.TryParse<ReleaseChannel>(value, ignoreCase: false, out _channel)
                || !Enum.IsDefined(_channel))
            {
                throw new InvalidOperationException("Release channel must be Development or Production.");
            }
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Release channel file is unavailable: {path}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Release channel file is unavailable: {path}", exception);
        }
    }

    /// <inheritdoc />
    public ReleaseChannel GetChannel() => _channel;
}
