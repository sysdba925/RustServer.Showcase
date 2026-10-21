using DotNetEnv;

namespace RustServer.Auth.Infrastructure.Configuration;

public static class EnvReader
{
    public static void Load()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, ".env");

        if (!File.Exists(path))
        {
            path = Path.Combine(
                Directory.GetCurrentDirectory(), ".env");
        }

        if (File.Exists(path))
        {
            // Environment variables supplied by the host (for example, a CI
            // secret store) must take precedence over a developer's .env file.
            Env.NoClobber().Load(path);
        }
    }
}
