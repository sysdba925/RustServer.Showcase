using System.Data;
using Microsoft.EntityFrameworkCore;
using RustServer.Auth.Infrastructure.Persistence;

namespace RustServer.Auth.Infrastructure.Readiness;

public sealed class AuthDatabaseReadinessProbe : IAuthReadinessProbe
{
    private static readonly string[] RequiredTables =
    [
        "__EFMigrationsHistory",
        "AspNetRoleClaims",
        "AspNetRoles",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserRoles",
        "AspNetUsers",
        "AspNetUserTokens"
    ];

    private readonly AuthDbContext _database;
    private readonly ILogger<AuthDatabaseReadinessProbe> _logger;

    public AuthDatabaseReadinessProbe(
        AuthDbContext database,
        ILogger<AuthDatabaseReadinessProbe> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _database.Database.CanConnectAsync(cancellationToken))
            {
                return false;
            }

            var tables = await GetTablesAsync(cancellationToken);
            if (RequiredTables.Except(tables, StringComparer.Ordinal).Any())
            {
                return false;
            }

            var pendingMigrations = await _database.Database
                .GetPendingMigrationsAsync(cancellationToken);

            return !pendingMigrations.Any();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Authentication database readiness check failed.");
            return false;
        }
    }

    private async Task<HashSet<string>> GetTablesAsync(CancellationToken cancellationToken)
    {
        var connection = _database.Database.GetDbConnection();
        bool closeConnection = connection.State != ConnectionState.Open;

        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var tables = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
