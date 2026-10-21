namespace RustServer.PluginCatalog.Api.Infrastructure.MediaStorage;

public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";
    public const long DefaultMaxUploadBytes = 10 * 1024 * 1024;
    public const ulong DefaultMaxDecodedPixels = 25_000_000;
    public const int DefaultMaxFrameCount = 300;
    public const uint DefaultThumbnailMaxDimension = 480;

    public string? Provider { get; init; }

    public string? RootPath { get; init; }

    public long MaxUploadBytes { get; init; } = DefaultMaxUploadBytes;

    public ulong MaxDecodedPixels { get; init; } = DefaultMaxDecodedPixels;

    public int MaxFrameCount { get; init; } = DefaultMaxFrameCount;

    public uint ThumbnailMaxDimension { get; init; } = DefaultThumbnailMaxDimension;
}
