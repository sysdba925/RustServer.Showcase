namespace RustServer.Rcon;

/// <summary>
/// Validates configuration used to connect to Rust WebRCON.
/// </summary>
public static class RustServerRconOptionsValidator
{
    private const int MaximumResponseBytes = 1_048_576;
    private const int MaximumTimeoutMinutes = 5;

    /// <summary>
    /// Validates the supplied WebRCON connection options.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <exception cref="RustServerRconConfigurationException">
    /// Thrown when one or more configuration values are invalid.
    /// </exception>
    public static void Validate(RustServerRconOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new RustServerRconConfigurationException(
                $"Configuration value '{RustServerRconOptions.SectionName}:Host' is required.");
        }

        if (options.Port is < 1 or > 65_535)
        {
            throw new RustServerRconConfigurationException(
                $"Configuration value '{RustServerRconOptions.SectionName}:Port' must be a valid TCP port.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            throw new RustServerRconConfigurationException(
                $"Configuration value '{RustServerRconOptions.SectionName}:Password' is required.");
        }

        ValidateTimeout(options.ConnectTimeout, nameof(RustServerRconOptions.ConnectTimeout));
        ValidateTimeout(options.CommandTimeout, nameof(RustServerRconOptions.CommandTimeout));

        if (options.MaxResponseBytes is < 1 or > MaximumResponseBytes)
        {
            throw new RustServerRconConfigurationException(
                $"Configuration value '{RustServerRconOptions.SectionName}:MaxResponseBytes' must be between 1 and {MaximumResponseBytes}.");
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string propertyName)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(MaximumTimeoutMinutes))
        {
            throw new RustServerRconConfigurationException(
                $"Configuration value '{RustServerRconOptions.SectionName}:{propertyName}' must be greater than zero and no more than {MaximumTimeoutMinutes} minutes.");
        }
    }
}
