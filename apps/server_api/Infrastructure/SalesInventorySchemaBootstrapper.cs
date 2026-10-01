using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public static class SalesInventorySchemaBootstrapper
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
            $"Unsupported provider for sales/inventory schema: {provider}");
    }

    private static async Task EnsureSqliteAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
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
                "SalesPrice" TEXT NOT NULL,
                "DefaultPurchasePrice" TEXT NOT NULL,
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
                CONSTRAINT "FK_sales_inventory_settings_receivable"
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
                "Subtotal" TEXT NOT NULL,
                "DiscountTotal" TEXT NOT NULL,
                "TaxTotal" TEXT NOT NULL,
                "GrandTotal" TEXT NOT NULL,
                "CostTotal" TEXT NOT NULL,
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
                CONSTRAINT "FK_sales_invoices_detail_accounts"
                    FOREIGN KEY ("CustomerDetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_warehouses"
                    FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_journal"
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
                "Quantity" TEXT NOT NULL,
                "UnitPrice" TEXT NOT NULL,
                "DiscountAmount" TEXT NOT NULL,
                "TaxAmount" TEXT NOT NULL,
                "NetAmount" TEXT NOT NULL,
                "UnitCost" TEXT NOT NULL,
                "CostAmount" TEXT NOT NULL,
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
                "Quantity" TEXT NOT NULL,
                "UnitCost" TEXT NOT NULL,
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
    }

    private static async Task EnsurePostgresAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
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
                "SalesPrice" numeric(20,4) NOT NULL,
                "DefaultPurchasePrice" numeric(20,4) NOT NULL,
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
                CONSTRAINT "FK_sales_inventory_settings_receivable"
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
                "Subtotal" numeric(20,4) NOT NULL,
                "DiscountTotal" numeric(20,4) NOT NULL,
                "TaxTotal" numeric(20,4) NOT NULL,
                "GrandTotal" numeric(20,4) NOT NULL,
                "CostTotal" numeric(20,4) NOT NULL,
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
                CONSTRAINT "FK_sales_invoices_detail_accounts"
                    FOREIGN KEY ("CustomerDetailAccountId") REFERENCES detail_accounts ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_warehouses"
                    FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id")
                    ON DELETE RESTRICT,
                CONSTRAINT "FK_sales_invoices_journal"
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
                "Quantity" numeric(20,4) NOT NULL,
                "UnitPrice" numeric(20,4) NOT NULL,
                "DiscountAmount" numeric(20,4) NOT NULL,
                "TaxAmount" numeric(20,4) NOT NULL,
                "NetAmount" numeric(20,4) NOT NULL,
                "UnitCost" numeric(20,4) NOT NULL,
                "CostAmount" numeric(20,4) NOT NULL,
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
                "Quantity" numeric(20,4) NOT NULL,
                "UnitCost" numeric(20,4) NOT NULL,
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
