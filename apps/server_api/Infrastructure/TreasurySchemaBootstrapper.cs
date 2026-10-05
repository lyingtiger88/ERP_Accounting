using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public static class TreasurySchemaBootstrapper
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
        CREATE TABLE IF NOT EXISTS treasury_accounts (
            "Id" TEXT NOT NULL CONSTRAINT "PK_treasury_accounts" PRIMARY KEY,
            "CompanyId" TEXT NOT NULL,
            "Code" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Type" TEXT NOT NULL,
            "LedgerAccountId" TEXT NOT NULL,
            "CurrencyId" TEXT NULL,
            "IsActive" INTEGER NOT NULL DEFAULT 1,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("LedgerAccountId") REFERENCES accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_treasury_accounts_CompanyId_Code"
        ON treasury_accounts ("CompanyId", "Code");
        """,
        """
        CREATE TABLE IF NOT EXISTS treasury_transactions (
            "Id" TEXT NOT NULL CONSTRAINT "PK_treasury_transactions" PRIMARY KEY,
            "CompanyId" TEXT NOT NULL,
            "FiscalYearId" TEXT NOT NULL,
            "Number" TEXT NOT NULL,
            "DocumentDate" TEXT NOT NULL,
            "Type" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "FromTreasuryAccountId" TEXT NULL,
            "ToTreasuryAccountId" TEXT NULL,
            "CounterAccountId" TEXT NULL,
            "DetailAccountId" TEXT NULL,
            "CurrencyId" TEXT NULL,
            "ExchangeRate" TEXT NOT NULL DEFAULT '1',
            "Amount" TEXT NOT NULL,
            "Description" TEXT NULL,
            "AccountingJournalEntryId" TEXT NULL,
            "ReversalJournalEntryId" TEXT NULL,
            "CreatedByUserId" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "ReversedAt" TEXT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("FromTreasuryAccountId") REFERENCES treasury_accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("ToTreasuryAccountId") REFERENCES treasury_accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CounterAccountId") REFERENCES accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_treasury_transactions_CompanyId_Number"
        ON treasury_transactions ("CompanyId", "Number");
        """
    ];

    private static readonly string[] PostgresCommands =
    [
        """
        CREATE TABLE IF NOT EXISTS treasury_accounts (
            "Id" uuid NOT NULL CONSTRAINT "PK_treasury_accounts" PRIMARY KEY,
            "CompanyId" uuid NOT NULL,
            "Code" character varying(80) NOT NULL,
            "Name" character varying(250) NOT NULL,
            "Type" character varying(30) NOT NULL,
            "LedgerAccountId" uuid NOT NULL,
            "CurrencyId" uuid NULL,
            "IsActive" boolean NOT NULL DEFAULT TRUE,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("LedgerAccountId") REFERENCES accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_treasury_accounts_CompanyId_Code"
        ON treasury_accounts ("CompanyId", "Code");
        """,
        """
        CREATE TABLE IF NOT EXISTS treasury_transactions (
            "Id" uuid NOT NULL CONSTRAINT "PK_treasury_transactions" PRIMARY KEY,
            "CompanyId" uuid NOT NULL,
            "FiscalYearId" uuid NOT NULL,
            "Number" character varying(80) NOT NULL,
            "DocumentDate" date NOT NULL,
            "Type" character varying(30) NOT NULL,
            "Status" character varying(30) NOT NULL,
            "FromTreasuryAccountId" uuid NULL,
            "ToTreasuryAccountId" uuid NULL,
            "CounterAccountId" uuid NULL,
            "DetailAccountId" uuid NULL,
            "CurrencyId" uuid NULL,
            "ExchangeRate" numeric(24,8) NOT NULL DEFAULT 1,
            "Amount" numeric(20,4) NOT NULL,
            "Description" character varying(500) NULL,
            "AccountingJournalEntryId" uuid NULL,
            "ReversalJournalEntryId" uuid NULL,
            "CreatedByUserId" uuid NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "ReversedAt" timestamp with time zone NULL,
            FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("FromTreasuryAccountId") REFERENCES treasury_accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("ToTreasuryAccountId") REFERENCES treasury_accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CounterAccountId") REFERENCES accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
            FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_treasury_transactions_CompanyId_Number"
        ON treasury_transactions ("CompanyId", "Number");
        """
    ];
}
