using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public static class CurrencySchemaBootstrapper
{
    public static async Task EnsureAsync(
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
            $"Unsupported provider for currency schema bootstrap: {provider}");
    }

    private static async Task EnsureSqliteAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS currencies (
                "Id" TEXT NOT NULL CONSTRAINT "PK_currencies" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Symbol" TEXT NULL,
                "DecimalPlaces" INTEGER NOT NULL DEFAULT 2,
                "IsBase" INTEGER NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_currencies_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_currencies_CompanyId_Code"
            ON currencies ("CompanyId", "Code");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_currencies_CompanyId_IsBase"
            ON currencies ("CompanyId", "IsBase");
            """,
            """
            CREATE TABLE IF NOT EXISTS currency_exchange_rates (
                "Id" TEXT NOT NULL
                    CONSTRAINT "PK_currency_exchange_rates" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "CurrencyId" TEXT NOT NULL,
                "RateDate" TEXT NOT NULL,
                "Source" TEXT NOT NULL,
                "BuyRate" TEXT NOT NULL,
                "SellRate" TEXT NOT NULL,
                "AccountingRate" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_currency_exchange_rates_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_currency_exchange_rates_currencies"
                    FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_currency_exchange_rates_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_currency_exchange_rates_Company_Currency_Date"
            ON currency_exchange_rates (
                "CompanyId",
                "CurrencyId",
                "RateDate"
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_line_currencies (
                "JournalLineId" TEXT NOT NULL
                    CONSTRAINT "PK_journal_line_currencies" PRIMARY KEY,
                "CurrencyId" TEXT NOT NULL,
                "ForeignDebit" TEXT NOT NULL,
                "ForeignCredit" TEXT NOT NULL,
                "ExchangeRate" TEXT NOT NULL,
                CONSTRAINT "FK_journal_line_currencies_lines"
                    FOREIGN KEY ("JournalLineId") REFERENCES journal_lines ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_line_currencies_currencies"
                    FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_currencies_CurrencyId"
            ON journal_line_currencies ("CurrencyId");
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(
                command,
                cancellationToken);
        }
    }

    private static async Task EnsurePostgresAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS currencies (
                "Id" uuid NOT NULL CONSTRAINT "PK_currencies" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Code" character varying(12) NOT NULL,
                "Name" character varying(120) NOT NULL,
                "Symbol" character varying(20) NULL,
                "DecimalPlaces" integer NOT NULL DEFAULT 2,
                "IsBase" boolean NOT NULL DEFAULT FALSE,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_currencies_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_currencies_CompanyId_Code"
            ON currencies ("CompanyId", "Code");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_currencies_CompanyId_IsBase"
            ON currencies ("CompanyId", "IsBase");
            """,
            """
            CREATE TABLE IF NOT EXISTS currency_exchange_rates (
                "Id" uuid NOT NULL
                    CONSTRAINT "PK_currency_exchange_rates" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "CurrencyId" uuid NOT NULL,
                "RateDate" date NOT NULL,
                "Source" character varying(120) NOT NULL,
                "BuyRate" numeric(24, 8) NOT NULL,
                "SellRate" numeric(24, 8) NOT NULL,
                "AccountingRate" numeric(24, 8) NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_currency_exchange_rates_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_currency_exchange_rates_currencies"
                    FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_currency_exchange_rates_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_currency_exchange_rates_Company_Currency_Date"
            ON currency_exchange_rates (
                "CompanyId",
                "CurrencyId",
                "RateDate"
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS journal_line_currencies (
                "JournalLineId" uuid NOT NULL
                    CONSTRAINT "PK_journal_line_currencies" PRIMARY KEY,
                "CurrencyId" uuid NOT NULL,
                "ForeignDebit" numeric(24, 8) NOT NULL,
                "ForeignCredit" numeric(24, 8) NOT NULL,
                "ExchangeRate" numeric(24, 8) NOT NULL,
                CONSTRAINT "FK_journal_line_currencies_lines"
                    FOREIGN KEY ("JournalLineId") REFERENCES journal_lines ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_line_currencies_currencies"
                    FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_currencies_CurrencyId"
            ON journal_line_currencies ("CurrencyId");
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(
                command,
                cancellationToken);
        }
    }
}
