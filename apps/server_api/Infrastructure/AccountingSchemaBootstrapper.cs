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
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }
    }
}
