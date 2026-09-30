using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public static class AccountingSchemaBootstrapper
{
    public static async Task EnsureExtensionsAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var provider = db.Database.ProviderName ?? string.Empty;

        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSqliteAsync(db, cancellationToken);
            return;
        }

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await EnsurePostgresAsync(db, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported provider for accounting schema bootstrap: {provider}");
    }

    private static async Task EnsureSqliteAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS fiscal_years (
                "Id" TEXT NOT NULL CONSTRAINT "PK_fiscal_years" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "PersianYear" INTEGER NOT NULL,
                "StartDate" TEXT NOT NULL,
                "EndDate" TEXT NOT NULL,
                "IsDefault" INTEGER NOT NULL DEFAULT 0,
                "IsClosed" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_fiscal_years_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_fiscal_years_CompanyId_Name"
            ON fiscal_years ("CompanyId", "Name");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_fiscal_years_CompanyId_StartDate_EndDate"
            ON fiscal_years ("CompanyId", "StartDate", "EndDate");
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_accounts (
                "Id" TEXT NOT NULL CONSTRAINT "PK_detail_accounts" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "NationalId" TEXT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_detail_accounts_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_detail_accounts_CompanyId_Code"
            ON detail_accounts ("CompanyId", "Code");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_accounts_CompanyId_Name"
            ON detail_accounts ("CompanyId", "Name");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_entry_fiscal_years (
                "JournalEntryId" TEXT NOT NULL
                    CONSTRAINT "PK_journal_entry_fiscal_years" PRIMARY KEY,
                "FiscalYearId" TEXT NOT NULL,
                CONSTRAINT "FK_journal_entry_fiscal_years_journal_entries"
                    FOREIGN KEY ("JournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_entry_fiscal_years_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_entry_fiscal_years_FiscalYearId"
            ON journal_entry_fiscal_years ("FiscalYearId");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_line_dimensions (
                "JournalLineId" TEXT NOT NULL
                    CONSTRAINT "PK_journal_line_dimensions" PRIMARY KEY,
                "DetailAccountId" TEXT NULL,
                CONSTRAINT "FK_journal_line_dimensions_journal_lines"
                    FOREIGN KEY ("JournalLineId") REFERENCES journal_lines ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_line_dimensions_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_dimensions_DetailAccountId"
            ON journal_line_dimensions ("DetailAccountId");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_sync_receipts (
                "Id" TEXT NOT NULL CONSTRAINT "PK_journal_sync_receipts" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "ChangeId" TEXT NOT NULL,
                "LocalDocumentId" TEXT NOT NULL,
                "JournalEntryId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_journal_sync_receipts_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_sync_receipts_journal_entries_JournalEntryId"
                    FOREIGN KEY ("JournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_sync_receipts_CompanyId_ChangeId"
            ON journal_sync_receipts ("CompanyId", "ChangeId");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_sync_receipts_CompanyId_LocalDocumentId"
            ON journal_sync_receipts ("CompanyId", "LocalDocumentId");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_sync_receipts_JournalEntryId"
            ON journal_sync_receipts ("JournalEntryId");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_server_changes (
                "Sequence" INTEGER NOT NULL
                    CONSTRAINT "PK_journal_server_changes"
                    PRIMARY KEY AUTOINCREMENT,
                "CompanyId" TEXT NOT NULL,
                "JournalEntryId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_journal_server_changes_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_server_changes_journal_entries_JournalEntryId"
                    FOREIGN KEY ("JournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_server_changes_CompanyId_Sequence"
            ON journal_server_changes ("CompanyId", "Sequence");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_server_changes_JournalEntryId"
            ON journal_server_changes ("JournalEntryId");
            """,
            """
            INSERT INTO journal_server_changes (
                "CompanyId",
                "JournalEntryId",
                "CreatedAt"
            )
            SELECT
                j."CompanyId",
                j."Id",
                j."CreatedAt"
            FROM journal_entries j
            WHERE NOT EXISTS (
                SELECT 1
                FROM journal_server_changes c
                WHERE c."JournalEntryId" = j."Id"
            )
            ORDER BY j."CreatedAt", j."Id";
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_account_sync_states (
                "DetailAccountId" TEXT NOT NULL
                    CONSTRAINT "PK_detail_account_sync_states" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Revision" INTEGER NOT NULL DEFAULT 1,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_detail_account_sync_states_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_detail_account_sync_states_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_account_sync_states_CompanyId_Revision"
            ON detail_account_sync_states ("CompanyId", "Revision");
            """,
            """
            INSERT INTO detail_account_sync_states (
                "DetailAccountId",
                "CompanyId",
                "Revision",
                "UpdatedAt"
            )
            SELECT
                d."Id",
                d."CompanyId",
                1,
                d."CreatedAt"
            FROM detail_accounts d
            WHERE NOT EXISTS (
                SELECT 1
                FROM detail_account_sync_states s
                WHERE s."DetailAccountId" = d."Id"
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_account_sync_receipts (
                "Id" TEXT NOT NULL
                    CONSTRAINT "PK_detail_account_sync_receipts" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "ChangeId" TEXT NOT NULL,
                "DetailAccountId" TEXT NOT NULL,
                "AppliedRevision" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_detail_account_sync_receipts_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_detail_account_sync_receipts_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_detail_account_sync_receipts_CompanyId_ChangeId"
            ON detail_account_sync_receipts ("CompanyId", "ChangeId");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_account_sync_receipts_EntityRevision"
            ON detail_account_sync_receipts (
                "CompanyId",
                "DetailAccountId",
                "AppliedRevision"
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_account_server_changes (
                "Sequence" INTEGER NOT NULL
                    CONSTRAINT "PK_detail_account_server_changes"
                    PRIMARY KEY AUTOINCREMENT,
                "CompanyId" TEXT NOT NULL,
                "DetailAccountId" TEXT NOT NULL,
                "Revision" INTEGER NOT NULL,
                "Operation" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_detail_account_server_changes_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_detail_account_server_changes_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_account_server_changes_CompanyId_Sequence"
            ON detail_account_server_changes ("CompanyId", "Sequence");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_detail_account_server_changes_EntityRevision"
            ON detail_account_server_changes ("DetailAccountId", "Revision");
            """,
            """
            INSERT INTO detail_account_server_changes (
                "CompanyId",
                "DetailAccountId",
                "Revision",
                "Operation",
                "CreatedAt"
            )
            SELECT
                s."CompanyId",
                s."DetailAccountId",
                s."Revision",
                'Upsert',
                s."UpdatedAt"
            FROM detail_account_sync_states s
            WHERE NOT EXISTS (
                SELECT 1
                FROM detail_account_server_changes c
                WHERE c."DetailAccountId" = s."DetailAccountId"
                  AND c."Revision" = s."Revision"
            )
            ORDER BY s."UpdatedAt", s."DetailAccountId";
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }
    }

    private static async Task EnsurePostgresAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS fiscal_years (
                "Id" uuid NOT NULL CONSTRAINT "PK_fiscal_years" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "PersianYear" integer NOT NULL,
                "StartDate" date NOT NULL,
                "EndDate" date NOT NULL,
                "IsDefault" boolean NOT NULL DEFAULT FALSE,
                "IsClosed" boolean NOT NULL DEFAULT FALSE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_fiscal_years_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_fiscal_years_CompanyId_Name"
            ON fiscal_years ("CompanyId", "Name");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_fiscal_years_CompanyId_StartDate_EndDate"
            ON fiscal_years ("CompanyId", "StartDate", "EndDate");
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_accounts (
                "Id" uuid NOT NULL CONSTRAINT "PK_detail_accounts" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Code" character varying(50) NOT NULL,
                "Name" character varying(250) NOT NULL,
                "Type" character varying(30) NOT NULL,
                "NationalId" character varying(50) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_detail_accounts_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_detail_accounts_CompanyId_Code"
            ON detail_accounts ("CompanyId", "Code");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_accounts_CompanyId_Name"
            ON detail_accounts ("CompanyId", "Name");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_entry_fiscal_years (
                "JournalEntryId" uuid NOT NULL
                    CONSTRAINT "PK_journal_entry_fiscal_years" PRIMARY KEY,
                "FiscalYearId" uuid NOT NULL,
                CONSTRAINT "FK_journal_entry_fiscal_years_journal_entries"
                    FOREIGN KEY ("JournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_entry_fiscal_years_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_entry_fiscal_years_FiscalYearId"
            ON journal_entry_fiscal_years ("FiscalYearId");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_line_dimensions (
                "JournalLineId" uuid NOT NULL
                    CONSTRAINT "PK_journal_line_dimensions" PRIMARY KEY,
                "DetailAccountId" uuid NULL,
                CONSTRAINT "FK_journal_line_dimensions_journal_lines"
                    FOREIGN KEY ("JournalLineId") REFERENCES journal_lines ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_line_dimensions_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_dimensions_DetailAccountId"
            ON journal_line_dimensions ("DetailAccountId");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_sync_receipts (
                "Id" uuid NOT NULL CONSTRAINT "PK_journal_sync_receipts" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "ChangeId" character varying(100) NOT NULL,
                "LocalDocumentId" character varying(100) NOT NULL,
                "JournalEntryId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_journal_sync_receipts_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_sync_receipts_journal_entries_JournalEntryId"
                    FOREIGN KEY ("JournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_sync_receipts_CompanyId_ChangeId"
            ON journal_sync_receipts ("CompanyId", "ChangeId");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_sync_receipts_CompanyId_LocalDocumentId"
            ON journal_sync_receipts ("CompanyId", "LocalDocumentId");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_sync_receipts_JournalEntryId"
            ON journal_sync_receipts ("JournalEntryId");
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_server_changes (
                "Sequence" bigint GENERATED BY DEFAULT AS IDENTITY
                    CONSTRAINT "PK_journal_server_changes" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "JournalEntryId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_journal_server_changes_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_server_changes_journal_entries_JournalEntryId"
                    FOREIGN KEY ("JournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_server_changes_CompanyId_Sequence"
            ON journal_server_changes ("CompanyId", "Sequence");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_server_changes_JournalEntryId"
            ON journal_server_changes ("JournalEntryId");
            """,
            """
            INSERT INTO journal_server_changes (
                "CompanyId",
                "JournalEntryId",
                "CreatedAt"
            )
            SELECT
                j."CompanyId",
                j."Id",
                j."CreatedAt"
            FROM journal_entries j
            WHERE NOT EXISTS (
                SELECT 1
                FROM journal_server_changes c
                WHERE c."JournalEntryId" = j."Id"
            )
            ORDER BY j."CreatedAt", j."Id";
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_account_sync_states (
                "DetailAccountId" uuid NOT NULL
                    CONSTRAINT "PK_detail_account_sync_states" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Revision" bigint NOT NULL DEFAULT 1,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_detail_account_sync_states_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_detail_account_sync_states_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_account_sync_states_CompanyId_Revision"
            ON detail_account_sync_states ("CompanyId", "Revision");
            """,
            """
            INSERT INTO detail_account_sync_states (
                "DetailAccountId",
                "CompanyId",
                "Revision",
                "UpdatedAt"
            )
            SELECT
                d."Id",
                d."CompanyId",
                1,
                d."CreatedAt"
            FROM detail_accounts d
            WHERE NOT EXISTS (
                SELECT 1
                FROM detail_account_sync_states s
                WHERE s."DetailAccountId" = d."Id"
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_account_sync_receipts (
                "Id" uuid NOT NULL
                    CONSTRAINT "PK_detail_account_sync_receipts" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "ChangeId" character varying(100) NOT NULL,
                "DetailAccountId" uuid NOT NULL,
                "AppliedRevision" bigint NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_detail_account_sync_receipts_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_detail_account_sync_receipts_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_detail_account_sync_receipts_CompanyId_ChangeId"
            ON detail_account_sync_receipts ("CompanyId", "ChangeId");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_account_sync_receipts_EntityRevision"
            ON detail_account_sync_receipts (
                "CompanyId",
                "DetailAccountId",
                "AppliedRevision"
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS detail_account_server_changes (
                "Sequence" bigint GENERATED BY DEFAULT AS IDENTITY
                    CONSTRAINT "PK_detail_account_server_changes" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "DetailAccountId" uuid NOT NULL,
                "Revision" bigint NOT NULL,
                "Operation" character varying(30) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_detail_account_server_changes_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_detail_account_server_changes_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_detail_account_server_changes_CompanyId_Sequence"
            ON detail_account_server_changes ("CompanyId", "Sequence");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_detail_account_server_changes_EntityRevision"
            ON detail_account_server_changes ("DetailAccountId", "Revision");
            """,
            """
            INSERT INTO detail_account_server_changes (
                "CompanyId",
                "DetailAccountId",
                "Revision",
                "Operation",
                "CreatedAt"
            )
            SELECT
                s."CompanyId",
                s."DetailAccountId",
                s."Revision",
                'Upsert',
                s."UpdatedAt"
            FROM detail_account_sync_states s
            WHERE NOT EXISTS (
                SELECT 1
                FROM detail_account_server_changes c
                WHERE c."DetailAccountId" = s."DetailAccountId"
                  AND c."Revision" = s."Revision"
            )
            ORDER BY s."UpdatedAt", s."DetailAccountId";
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }
    }
}
