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
            CREATE TABLE IF NOT EXISTS fiscal_periods (
                "Id" TEXT NOT NULL
                    CONSTRAINT "PK_fiscal_periods" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "FiscalYearId" TEXT NOT NULL,
                "PeriodNumber" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "StartDate" TEXT NOT NULL,
                "EndDate" TEXT NOT NULL,
                "IsClosed" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_fiscal_periods_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_fiscal_periods_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_fiscal_periods_FiscalYearId_PeriodNumber"
            ON fiscal_periods ("FiscalYearId", "PeriodNumber");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_fiscal_periods_CompanyId_StartDate_EndDate"
            ON fiscal_periods ("CompanyId", "StartDate", "EndDate");
            """,
            """
            CREATE TABLE IF NOT EXISTS fiscal_year_closings (
                "FiscalYearId" TEXT NOT NULL
                    CONSTRAINT "PK_fiscal_year_closings" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "ClosingJournalEntryId" TEXT NULL,
                "RetainedEarningsAccountId" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "NetResult" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_fiscal_year_closings_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_fiscal_year_closings_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_fiscal_year_closings_journals"
                    FOREIGN KEY ("ClosingJournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_fiscal_year_closings_accounts"
                    FOREIGN KEY ("RetainedEarningsAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_fiscal_year_closings_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_fiscal_year_closings_ClosingJournalEntryId"
            ON fiscal_year_closings ("ClosingJournalEntryId");
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
            CREATE TABLE IF NOT EXISTS cost_centers (
                "Id" TEXT NOT NULL CONSTRAINT "PK_cost_centers" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_cost_centers_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_cost_centers_CompanyId_Code"
            ON cost_centers ("CompanyId", "Code");
            """,
            """
            CREATE TABLE IF NOT EXISTS accounting_projects (
                "Id" TEXT NOT NULL CONSTRAINT "PK_accounting_projects" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_accounting_projects_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_accounting_projects_CompanyId_Code"
            ON accounting_projects ("CompanyId", "Code");
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
                "CostCenterId" TEXT NULL,
                "ProjectId" TEXT NULL,
                CONSTRAINT "FK_journal_line_dimensions_journal_lines"
                    FOREIGN KEY ("JournalLineId") REFERENCES journal_lines ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_line_dimensions_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_line_dimensions_cost_centers"
                    FOREIGN KEY ("CostCenterId") REFERENCES cost_centers ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_line_dimensions_accounting_projects"
                    FOREIGN KEY ("ProjectId") REFERENCES accounting_projects ("Id")
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
,
            """
            CREATE TABLE IF NOT EXISTS journal_reversal_links (
                "OriginalJournalEntryId" TEXT NOT NULL
                    CONSTRAINT "PK_journal_reversal_links" PRIMARY KEY,
                "ReversalJournalEntryId" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "Reason" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_journal_reversal_links_original"
                    FOREIGN KEY ("OriginalJournalEntryId")
                    REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_reversal_links_reversal"
                    FOREIGN KEY ("ReversalJournalEntryId")
                    REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_reversal_links_users"
                    FOREIGN KEY ("CreatedByUserId")
                    REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_reversal_links_ReversalJournalEntryId"
            ON journal_reversal_links ("ReversalJournalEntryId");
            """,
            """
            CREATE TABLE IF NOT EXISTS accounting_audit_logs (
                "Id" TEXT NOT NULL
                    CONSTRAINT "PK_accounting_audit_logs" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityId" TEXT NOT NULL,
                "Action" TEXT NOT NULL,
                "Reason" TEXT NULL,
                "PayloadJson" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_accounting_audit_logs_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_accounting_audit_logs_users"
                    FOREIGN KEY ("UserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_accounting_audit_logs_Entity"
            ON accounting_audit_logs (
                "CompanyId",
                "EntityType",
                "EntityId"
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_accounting_audit_logs_CompanyId_CreatedAt"
            ON accounting_audit_logs ("CompanyId", "CreatedAt");
            """
,
            """
            CREATE TABLE IF NOT EXISTS store_products (
                "Id" TEXT NOT NULL CONSTRAINT "PK_store_products" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Sku" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Barcode" TEXT NULL,
                "UnitName" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "TrackInventory" INTEGER NOT NULL DEFAULT 1,
                "SalesPrice" TEXT NOT NULL DEFAULT '0',
                "DefaultPurchasePrice" TEXT NOT NULL DEFAULT '0',
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_store_products_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_store_products_CompanyId_Sku"
            ON store_products ("CompanyId", "Sku");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_store_products_CompanyId_Barcode"
            ON store_products ("CompanyId", "Barcode");
            """,
            """
            CREATE TABLE IF NOT EXISTS warehouses (
                "Id" TEXT NOT NULL CONSTRAINT "PK_warehouses" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_warehouses_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_warehouses_CompanyId_Code"
            ON warehouses ("CompanyId", "Code");
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_inventory_settings (
                "CompanyId" TEXT NOT NULL
                    CONSTRAINT "PK_sales_inventory_settings" PRIMARY KEY,
                "ReceivablesAccountId" TEXT NOT NULL,
                "CashAccountId" TEXT NOT NULL,
                "SalesRevenueAccountId" TEXT NOT NULL,
                "InventoryAccountId" TEXT NOT NULL,
                "CostOfGoodsSoldAccountId" TEXT NOT NULL,
                "SalesTaxPayableAccountId" TEXT NOT NULL,
                "PreventNegativeStock" INTEGER NOT NULL DEFAULT 1,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_sales_inventory_settings_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_sales_inventory_settings_receivables"
                    FOREIGN KEY ("ReceivablesAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_cash"
                    FOREIGN KEY ("CashAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_revenue"
                    FOREIGN KEY ("SalesRevenueAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_inventory"
                    FOREIGN KEY ("InventoryAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_cogs"
                    FOREIGN KEY ("CostOfGoodsSoldAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_tax"
                    FOREIGN KEY ("SalesTaxPayableAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_invoices (
                "Id" TEXT NOT NULL CONSTRAINT "PK_sales_invoices" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "FiscalYearId" TEXT NOT NULL,
                "Number" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "CustomerDetailAccountId" TEXT NULL,
                "WarehouseId" TEXT NOT NULL,
                "PaymentType" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Description" TEXT NULL,
                "Subtotal" TEXT NOT NULL DEFAULT '0',
                "DiscountTotal" TEXT NOT NULL DEFAULT '0',
                "TaxTotal" TEXT NOT NULL DEFAULT '0',
                "GrandTotal" TEXT NOT NULL DEFAULT '0',
                "CostTotal" TEXT NOT NULL DEFAULT '0',
                "AccountingJournalEntryId" TEXT NULL,
                "ReversalJournalEntryId" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "PostedAt" TEXT NULL,
                CONSTRAINT "FK_sales_invoices_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_sales_invoices_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_customers"
                    FOREIGN KEY ("CustomerDetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_warehouses"
                    FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_accounting_journal"
                    FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_reversal_journal"
                    FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_sales_invoices_CompanyId_Number"
            ON sales_invoices ("CompanyId", "Number");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_sales_invoices_CompanyId_DocumentDate"
            ON sales_invoices ("CompanyId", "DocumentDate");
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_invoice_lines (
                "Id" TEXT NOT NULL CONSTRAINT "PK_sales_invoice_lines" PRIMARY KEY,
                "SalesInvoiceId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL DEFAULT '0',
                "UnitPrice" TEXT NOT NULL DEFAULT '0',
                "DiscountAmount" TEXT NOT NULL DEFAULT '0',
                "TaxAmount" TEXT NOT NULL DEFAULT '0',
                "NetAmount" TEXT NOT NULL DEFAULT '0',
                "UnitCost" TEXT NOT NULL DEFAULT '0',
                "CostAmount" TEXT NOT NULL DEFAULT '0',
                CONSTRAINT "FK_sales_invoice_lines_invoices"
                    FOREIGN KEY ("SalesInvoiceId") REFERENCES sales_invoices ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_sales_invoice_lines_products"
                    FOREIGN KEY ("ProductId") REFERENCES store_products ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_sales_invoice_lines_ProductId"
            ON sales_invoice_lines ("ProductId");
            """,
            """
            CREATE TABLE IF NOT EXISTS stock_movements (
                "Id" TEXT NOT NULL CONSTRAINT "PK_stock_movements" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "WarehouseId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL DEFAULT '0',
                "UnitCost" TEXT NOT NULL DEFAULT '0',
                "ReferenceType" TEXT NULL,
                "ReferenceId" TEXT NULL,
                "Description" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_stock_movements_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_stock_movements_warehouses"
                    FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_stock_movements_products"
                    FOREIGN KEY ("ProductId") REFERENCES store_products ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_stock_movements_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_stock_movements_Company_Warehouse_Product_Date"
            ON stock_movements (
                "CompanyId",
                "WarehouseId",
                "ProductId",
                "DocumentDate"
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_stock_movements_Reference"
            ON stock_movements ("ReferenceType", "ReferenceId");
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }

        await EnsureSqliteColumnAsync(
            db,
            "journal_line_dimensions",
            "CostCenterId",
            "ALTER TABLE journal_line_dimensions ADD COLUMN \"CostCenterId\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db,
            "journal_line_dimensions",
            "ProjectId",
            "ALTER TABLE journal_line_dimensions ADD COLUMN \"ProjectId\" TEXT NULL;",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_journal_line_dimensions_CostCenterId\" ON journal_line_dimensions (\"CostCenterId\");",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_journal_line_dimensions_ProjectId\" ON journal_line_dimensions (\"ProjectId\");",
            cancellationToken);
    }

    private static async Task EnsureSqliteColumnAsync(
        AppDbContext db,
        string tableName,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose =
            connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM pragma_table_info('{tableName.Replace("'", "''")}') WHERE name = $name;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = columnName;
            command.Parameters.Add(parameter);

            var exists = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken)) > 0;

            if (!exists)
            {
                await db.Database.ExecuteSqlRawAsync(
                    alterSql,
                    cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
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
            CREATE TABLE IF NOT EXISTS fiscal_periods (
                "Id" uuid NOT NULL
                    CONSTRAINT "PK_fiscal_periods" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL,
                "PeriodNumber" integer NOT NULL,
                "Name" character varying(120) NOT NULL,
                "StartDate" date NOT NULL,
                "EndDate" date NOT NULL,
                "IsClosed" boolean NOT NULL DEFAULT FALSE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_fiscal_periods_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_fiscal_periods_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_fiscal_periods_FiscalYearId_PeriodNumber"
            ON fiscal_periods ("FiscalYearId", "PeriodNumber");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_fiscal_periods_CompanyId_StartDate_EndDate"
            ON fiscal_periods ("CompanyId", "StartDate", "EndDate");
            """,
            """
            CREATE TABLE IF NOT EXISTS fiscal_year_closings (
                "FiscalYearId" uuid NOT NULL
                    CONSTRAINT "PK_fiscal_year_closings" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "ClosingJournalEntryId" uuid NULL,
                "RetainedEarningsAccountId" uuid NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "NetResult" numeric(20, 4) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_fiscal_year_closings_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_fiscal_year_closings_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_fiscal_year_closings_journals"
                    FOREIGN KEY ("ClosingJournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_fiscal_year_closings_accounts"
                    FOREIGN KEY ("RetainedEarningsAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_fiscal_year_closings_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_fiscal_year_closings_ClosingJournalEntryId"
            ON fiscal_year_closings ("ClosingJournalEntryId")
            WHERE "ClosingJournalEntryId" IS NOT NULL;
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
            CREATE TABLE IF NOT EXISTS cost_centers (
                "Id" uuid NOT NULL CONSTRAINT "PK_cost_centers" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Code" character varying(50) NOT NULL,
                "Name" character varying(250) NOT NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_cost_centers_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_cost_centers_CompanyId_Code"
            ON cost_centers ("CompanyId", "Code");
            """,
            """
            CREATE TABLE IF NOT EXISTS accounting_projects (
                "Id" uuid NOT NULL CONSTRAINT "PK_accounting_projects" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Code" character varying(50) NOT NULL,
                "Name" character varying(250) NOT NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_accounting_projects_companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_accounting_projects_CompanyId_Code"
            ON accounting_projects ("CompanyId", "Code");
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
                "CostCenterId" uuid NULL,
                "ProjectId" uuid NULL,
                CONSTRAINT "FK_journal_line_dimensions_journal_lines"
                    FOREIGN KEY ("JournalLineId") REFERENCES journal_lines ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_journal_line_dimensions_detail_accounts"
                    FOREIGN KEY ("DetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_line_dimensions_cost_centers"
                    FOREIGN KEY ("CostCenterId") REFERENCES cost_centers ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_line_dimensions_accounting_projects"
                    FOREIGN KEY ("ProjectId") REFERENCES accounting_projects ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            ALTER TABLE journal_line_dimensions
                ADD COLUMN IF NOT EXISTS "CostCenterId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "ProjectId" uuid NULL;
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_dimensions_DetailAccountId"
            ON journal_line_dimensions ("DetailAccountId");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_dimensions_CostCenterId"
            ON journal_line_dimensions ("CostCenterId");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_journal_line_dimensions_ProjectId"
            ON journal_line_dimensions ("ProjectId");
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
,
            """
            CREATE TABLE IF NOT EXISTS journal_reversal_links (
                "OriginalJournalEntryId" uuid NOT NULL
                    CONSTRAINT "PK_journal_reversal_links" PRIMARY KEY,
                "ReversalJournalEntryId" uuid NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "Reason" character varying(1000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_journal_reversal_links_original"
                    FOREIGN KEY ("OriginalJournalEntryId")
                    REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_reversal_links_reversal"
                    FOREIGN KEY ("ReversalJournalEntryId")
                    REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_journal_reversal_links_users"
                    FOREIGN KEY ("CreatedByUserId")
                    REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_journal_reversal_links_ReversalJournalEntryId"
            ON journal_reversal_links ("ReversalJournalEntryId");
            """,
            """
            CREATE TABLE IF NOT EXISTS accounting_audit_logs (
                "Id" uuid NOT NULL
                    CONSTRAINT "PK_accounting_audit_logs" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "EntityType" character varying(80) NOT NULL,
                "EntityId" uuid NOT NULL,
                "Action" character varying(80) NOT NULL,
                "Reason" character varying(1000) NULL,
                "PayloadJson" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_accounting_audit_logs_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_accounting_audit_logs_users"
                    FOREIGN KEY ("UserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_accounting_audit_logs_Entity"
            ON accounting_audit_logs (
                "CompanyId",
                "EntityType",
                "EntityId"
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_accounting_audit_logs_CompanyId_CreatedAt"
            ON accounting_audit_logs ("CompanyId", "CreatedAt");
            """
,
            """
            CREATE TABLE IF NOT EXISTS store_products (
                "Id" uuid NOT NULL CONSTRAINT "PK_store_products" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Sku" character varying(80) NOT NULL,
                "Name" character varying(250) NOT NULL,
                "Barcode" character varying(120) NULL,
                "UnitName" character varying(60) NOT NULL,
                "Kind" character varying(30) NOT NULL,
                "TrackInventory" boolean NOT NULL DEFAULT TRUE,
                "SalesPrice" numeric(20, 4) NOT NULL DEFAULT 0,
                "DefaultPurchasePrice" numeric(20, 4) NOT NULL DEFAULT 0,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_store_products_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_store_products_CompanyId_Sku"
            ON store_products ("CompanyId", "Sku");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_store_products_CompanyId_Barcode"
            ON store_products ("CompanyId", "Barcode");
            """,
            """
            CREATE TABLE IF NOT EXISTS warehouses (
                "Id" uuid NOT NULL CONSTRAINT "PK_warehouses" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Code" character varying(50) NOT NULL,
                "Name" character varying(200) NOT NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_warehouses_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_warehouses_CompanyId_Code"
            ON warehouses ("CompanyId", "Code");
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_inventory_settings (
                "CompanyId" uuid NOT NULL
                    CONSTRAINT "PK_sales_inventory_settings" PRIMARY KEY,
                "ReceivablesAccountId" uuid NOT NULL,
                "CashAccountId" uuid NOT NULL,
                "SalesRevenueAccountId" uuid NOT NULL,
                "InventoryAccountId" uuid NOT NULL,
                "CostOfGoodsSoldAccountId" uuid NOT NULL,
                "SalesTaxPayableAccountId" uuid NOT NULL,
                "PreventNegativeStock" boolean NOT NULL DEFAULT TRUE,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_sales_inventory_settings_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_sales_inventory_settings_receivables"
                    FOREIGN KEY ("ReceivablesAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_cash"
                    FOREIGN KEY ("CashAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_revenue"
                    FOREIGN KEY ("SalesRevenueAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_inventory"
                    FOREIGN KEY ("InventoryAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_cogs"
                    FOREIGN KEY ("CostOfGoodsSoldAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_inventory_settings_tax"
                    FOREIGN KEY ("SalesTaxPayableAccountId") REFERENCES ledger_accounts ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_invoices (
                "Id" uuid NOT NULL CONSTRAINT "PK_sales_invoices" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL,
                "Number" character varying(80) NOT NULL,
                "DocumentDate" date NOT NULL,
                "CustomerDetailAccountId" uuid NULL,
                "WarehouseId" uuid NOT NULL,
                "PaymentType" character varying(30) NOT NULL,
                "Status" character varying(30) NOT NULL,
                "Description" character varying(500) NULL,
                "Subtotal" numeric(20, 4) NOT NULL DEFAULT 0,
                "DiscountTotal" numeric(20, 4) NOT NULL DEFAULT 0,
                "TaxTotal" numeric(20, 4) NOT NULL DEFAULT 0,
                "GrandTotal" numeric(20, 4) NOT NULL DEFAULT 0,
                "CostTotal" numeric(20, 4) NOT NULL DEFAULT 0,
                "AccountingJournalEntryId" uuid NULL,
                "ReversalJournalEntryId" uuid NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "PostedAt" timestamp with time zone NULL,
                CONSTRAINT "FK_sales_invoices_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_sales_invoices_fiscal_years"
                    FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_customers"
                    FOREIGN KEY ("CustomerDetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_warehouses"
                    FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_accounting_journal"
                    FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_reversal_journal"
                    FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
                "IX_sales_invoices_CompanyId_Number"
            ON sales_invoices ("CompanyId", "Number");
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_sales_invoices_CompanyId_DocumentDate"
            ON sales_invoices ("CompanyId", "DocumentDate");
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_invoice_lines (
                "Id" uuid NOT NULL CONSTRAINT "PK_sales_invoice_lines" PRIMARY KEY,
                "SalesInvoiceId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Quantity" numeric(20, 4) NOT NULL DEFAULT 0,
                "UnitPrice" numeric(20, 4) NOT NULL DEFAULT 0,
                "DiscountAmount" numeric(20, 4) NOT NULL DEFAULT 0,
                "TaxAmount" numeric(20, 4) NOT NULL DEFAULT 0,
                "NetAmount" numeric(20, 4) NOT NULL DEFAULT 0,
                "UnitCost" numeric(20, 4) NOT NULL DEFAULT 0,
                "CostAmount" numeric(20, 4) NOT NULL DEFAULT 0,
                CONSTRAINT "FK_sales_invoice_lines_invoices"
                    FOREIGN KEY ("SalesInvoiceId") REFERENCES sales_invoices ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_sales_invoice_lines_products"
                    FOREIGN KEY ("ProductId") REFERENCES store_products ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_sales_invoice_lines_ProductId"
            ON sales_invoice_lines ("ProductId");
            """,
            """
            CREATE TABLE IF NOT EXISTS stock_movements (
                "Id" uuid NOT NULL CONSTRAINT "PK_stock_movements" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "WarehouseId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "DocumentDate" date NOT NULL,
                "Type" character varying(30) NOT NULL,
                "Quantity" numeric(20, 4) NOT NULL DEFAULT 0,
                "UnitCost" numeric(20, 4) NOT NULL DEFAULT 0,
                "ReferenceType" character varying(80) NULL,
                "ReferenceId" uuid NULL,
                "Description" character varying(500) NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "FK_stock_movements_companies"
                    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id")
                    ON DELETE CASCADE,
                CONSTRAINT "FK_stock_movements_warehouses"
                    FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_stock_movements_products"
                    FOREIGN KEY ("ProductId") REFERENCES store_products ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_stock_movements_users"
                    FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id")
                    ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_stock_movements_Company_Warehouse_Product_Date"
            ON stock_movements (
                "CompanyId",
                "WarehouseId",
                "ProductId",
                "DocumentDate"
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS
                "IX_stock_movements_Reference"
            ON stock_movements ("ReferenceType", "ReferenceId");
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }
    }
}
