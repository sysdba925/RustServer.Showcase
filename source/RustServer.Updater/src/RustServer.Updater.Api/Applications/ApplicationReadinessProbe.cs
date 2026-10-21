namespace RustServer.Updater.Api.Applications;

/// <summary>Declarative HTTP readiness check for a registered application.</summary>
public sealed record ApplicationReadinessProbe(
    string Url,
    int Attempts = 15,
    int TimeoutSeconds = 5,
    int DelaySeconds = 1);
