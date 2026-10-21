using System.Text;

namespace RustServer.Plugins.Features.PluginManagement.Operations;

internal static class OperationHistoryCursor
{
    public static string Encode(long startedAtTicks, Guid operationId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{startedAtTicks}:{operationId:D}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool TryDecode(string? cursor, out long startedAtTicks, out Guid operationId)
    {
        startedAtTicks = default;
        operationId = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(normalized)).Split(':', StringSplitOptions.None);
            return parts.Length == 2 &&
                long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out startedAtTicks) &&
                startedAtTicks > 0 &&
                Guid.TryParseExact(parts[1], "D", out operationId) &&
                operationId != Guid.Empty;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
