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
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var query = connection.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM pragma_table_info('trusted_devices') WHERE name = 'InstallationIdHash'";
                if (Convert.ToInt64(await query.ExecuteScalarAsync(cancellationToken)) == 0)
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE trusted_devices ADD COLUMN InstallationIdHash TEXT NULL", cancellationToken);
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
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
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_trusted_devices_UserId_InstallationIdHash ON trusted_devices (UserId, InstallationIdHash);",
        """
        CREATE TABLE IF NOT EXISTS device_pairing_challenges (
            "Id" TEXT NOT NULL CONSTRAINT "PK_device_pairing_challenges" PRIMARY KEY,
            "CompanyId" TEXT NOT NULL,
            "UserId" TEXT NOT NULL,
            "CodeHash" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "ExpiresAt" TEXT NOT NULL,
            "UsedAt" TEXT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_device_pairing_challenges_CodeHash"
        ON device_pairing_challenges ("CodeHash");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_device_pairing_challenges_UserId_ExpiresAt_UsedAt"
        ON device_pairing_challenges ("UserId", "ExpiresAt", "UsedAt");
        """,
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
        """,
        """
        CREATE TABLE IF NOT EXISTS security_audit_logs (
            "Id" TEXT NOT NULL CONSTRAINT "PK_security_audit_logs" PRIMARY KEY,
            "CompanyId" TEXT NULL,
            "UserId" TEXT NULL,
            "EventType" TEXT NOT NULL,
            "Outcome" TEXT NOT NULL,
            "Username" TEXT NULL,
            "Details" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE SET NULL
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_security_audit_logs_CompanyId_CreatedAt"
        ON security_audit_logs ("CompanyId", "CreatedAt");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_security_audit_logs_UserId_CreatedAt"
        ON security_audit_logs ("UserId", "CreatedAt");
        """,
        """
        CREATE TABLE IF NOT EXISTS zarr_company_licenses (
            "CompanyId" TEXT NOT NULL PRIMARY KEY,
            "AuthUserId" TEXT NOT NULL,
            "LinkedByUserId" TEXT NOT NULL,
            "LinkedAt" TEXT NOT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("LinkedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_zarr_company_licenses_AuthUserId"
        ON zarr_company_licenses ("AuthUserId");
        """,
        """
        CREATE TABLE IF NOT EXISTS user_mfa_credentials (
            "Id" TEXT NOT NULL CONSTRAINT "PK_user_mfa_credentials" PRIMARY KEY,
            "UserId" TEXT NOT NULL,
            "ProtectedSecret" TEXT NULL,
            "PendingProtectedSecret" TEXT NULL,
            "RecoveryCodeHashesJson" TEXT NOT NULL,
            "LastAcceptedTimeStep" INTEGER NULL,
            "FailedAttempts" INTEGER NOT NULL DEFAULT 0,
            "LockedUntil" TEXT NULL,
            "EnabledAt" TEXT NULL,
            "UpdatedAt" TEXT NOT NULL,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_mfa_credentials_UserId"
        ON user_mfa_credentials ("UserId");
        """,
    ];

    private static readonly string[] PostgresCommands =
    [
        "ALTER TABLE trusted_devices ADD COLUMN IF NOT EXISTS \"InstallationIdHash\" character varying(64) NULL;",
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_trusted_devices_UserId_InstallationIdHash\" ON trusted_devices (\"UserId\", \"InstallationIdHash\");",
        """
        CREATE TABLE IF NOT EXISTS device_pairing_challenges (
            "Id" uuid NOT NULL CONSTRAINT "PK_device_pairing_challenges" PRIMARY KEY,
            "CompanyId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "CodeHash" character varying(64) NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "ExpiresAt" timestamp with time zone NOT NULL,
            "UsedAt" timestamp with time zone NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_device_pairing_challenges_CodeHash"
        ON device_pairing_challenges ("CodeHash");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_device_pairing_challenges_UserId_ExpiresAt_UsedAt"
        ON device_pairing_challenges ("UserId", "ExpiresAt", "UsedAt");
        """,
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
        """,
        """
        CREATE TABLE IF NOT EXISTS security_audit_logs (
            "Id" uuid NOT NULL CONSTRAINT "PK_security_audit_logs" PRIMARY KEY,
            "CompanyId" uuid NULL,
            "UserId" uuid NULL,
            "EventType" character varying(80) NOT NULL,
            "Outcome" character varying(40) NOT NULL,
            "Username" character varying(120) NULL,
            "Details" character varying(1000) NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE SET NULL
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_security_audit_logs_CompanyId_CreatedAt"
        ON security_audit_logs ("CompanyId", "CreatedAt");
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_security_audit_logs_UserId_CreatedAt"
        ON security_audit_logs ("UserId", "CreatedAt");
        """,
        """
        CREATE TABLE IF NOT EXISTS zarr_company_licenses (
            "CompanyId" uuid NOT NULL PRIMARY KEY,
            "AuthUserId" uuid NOT NULL,
            "LinkedByUserId" uuid NOT NULL,
            "LinkedAt" timestamp with time zone NOT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("LinkedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS "IX_zarr_company_licenses_AuthUserId"
        ON zarr_company_licenses ("AuthUserId");
        """,
        """
        CREATE TABLE IF NOT EXISTS user_mfa_credentials (
            "Id" uuid NOT NULL CONSTRAINT "PK_user_mfa_credentials" PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "ProtectedSecret" character varying(2048) NULL,
            "PendingProtectedSecret" character varying(2048) NULL,
            "RecoveryCodeHashesJson" text NOT NULL,
            "LastAcceptedTimeStep" bigint NULL,
            "FailedAttempts" integer NOT NULL DEFAULT 0,
            "LockedUntil" timestamp with time zone NULL,
            "EnabledAt" timestamp with time zone NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_mfa_credentials_UserId"
        ON user_mfa_credentials ("UserId");
        """,
    ];
}
