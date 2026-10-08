using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public static class SecuritySchemaBootstrapper
{
    public static async Task EnsureAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var provider = db.Database.ProviderName ?? string.Empty;

        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var command in SqliteCommands)
            {
                await db.Database.ExecuteSqlRawAsync(
                    command,
                    cancellationToken);
            }
            return;
        }

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var command in PostgresCommands)
            {
                await db.Database.ExecuteSqlRawAsync(
                    command,
                    cancellationToken);
            }
        }
    }

    private static readonly string[] SqliteCommands =
    [
        """
        CREATE TABLE IF NOT EXISTS auth_sessions (
            "Id" TEXT NOT NULL CONSTRAINT "PK_auth_sessions" PRIMARY KEY,
            "CompanyId" TEXT NOT NULL,
            "UserId" TEXT NOT NULL,
            "DeviceId" TEXT NULL,
            "AccessTokenHash" TEXT NOT NULL,
            "RefreshTokenHash" TEXT NOT NULL,
            "AccessExpiresAt" TEXT NOT NULL,
            "RefreshExpiresAt" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "LastRefreshedAt" TEXT NOT NULL,
            "RevokedAt" TEXT NULL,
            "RevokeReason" TEXT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("DeviceId") REFERENCES trusted_devices ("Id") ON DELETE SET NULL
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_auth_sessions_AccessTokenHash"
        ON auth_sessions ("AccessTokenHash");
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_auth_sessions_RefreshTokenHash"
        ON auth_sessions ("RefreshTokenHash");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_auth_sessions_UserId_RevokedAt"
        ON auth_sessions ("UserId", "RevokedAt");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_auth_sessions_RefreshExpiresAt"
        ON auth_sessions ("RefreshExpiresAt");
        """
    ];

    private static readonly string[] PostgresCommands =
    [
        """
        CREATE TABLE IF NOT EXISTS auth_sessions (
            "Id" uuid NOT NULL CONSTRAINT "PK_auth_sessions" PRIMARY KEY,
            "CompanyId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "DeviceId" uuid NULL,
            "AccessTokenHash" character varying(128) NOT NULL,
            "RefreshTokenHash" character varying(128) NOT NULL,
            "AccessExpiresAt" timestamp with time zone NOT NULL,
            "RefreshExpiresAt" timestamp with time zone NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "LastRefreshedAt" timestamp with time zone NOT NULL,
            "RevokedAt" timestamp with time zone NULL,
            "RevokeReason" character varying(250) NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("DeviceId") REFERENCES trusted_devices ("Id") ON DELETE SET NULL
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_auth_sessions_AccessTokenHash"
        ON auth_sessions ("AccessTokenHash");
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_auth_sessions_RefreshTokenHash"
        ON auth_sessions ("RefreshTokenHash");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_auth_sessions_UserId_RevokedAt"
        ON auth_sessions ("UserId", "RevokedAt");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_auth_sessions_RefreshExpiresAt"
        ON auth_sessions ("RefreshExpiresAt");
        """
    ];
}
