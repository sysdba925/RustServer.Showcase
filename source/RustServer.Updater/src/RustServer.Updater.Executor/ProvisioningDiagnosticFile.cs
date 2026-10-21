using System.Text.Json;
using System.Text.RegularExpressions;

namespace RustServer.Updater.Executor;

// Only fixed identifiers from the verified hook are accepted, never process output.
internal sealed class ProvisioningDiagnosticFile : IDisposable
{
    private readonly string directory;
    public string Path { get; }

    public ProvisioningDiagnosticFile()
    {
        directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rustserver-provisioning-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Path = System.IO.Path.Combine(directory, "diagnostic.json");
    }

    public string Read()
    {
        const string fallback = "stage unknown; category unknown; reason diagnostic_unavailable;";
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists || info.LinkTarget is not null || info.Length is <= 0 or > 1024) return fallback;
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 1024) return fallback;
            var buffer = new byte[1025];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = stream.Read(buffer, count, buffer.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count is 0 or > 1024) return fallback;
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
                !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var v) || v != 1 ||
                !TryCode(root, "stage", out var stage) || !TryCode(root, "category", out var category) ||
                !TryCode(root, "reason", out var reason)) return fallback;
            return $"stage {stage}; category {category}; reason {reason};";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return fallback;
        }
    }

    private static bool TryCode(JsonElement root, string name, out string code)
    {
        code = "";
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return false;
        code = value.GetString() ?? "";
        return Regex.IsMatch(code, "\\A[a-z][a-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant);
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
