namespace RustServer.Auth.Infrastructure.Readiness;

public interface IAuthReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
