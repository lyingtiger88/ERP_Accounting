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
                "CurrencyId" TEXT NULL,
                "ExchangeRate" TEXT NOT NULL DEFAULT '1',
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
                CONSTRAINT "FK_sales_invoices_currencies"
                    FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id")
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
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_orders (
                "Id" TEXT NOT NULL CONSTRAINT "PK_purchase_orders" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "FiscalYearId" TEXT NOT NULL,
                "Number" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "ExpectedDate" TEXT NULL,
                "CurrencyId" TEXT NULL,
                "ExchangeRate" TEXT NOT NULL DEFAULT '1',
                "SupplierDetailAccountId" TEXT NOT NULL,
                "WarehouseId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Description" TEXT NULL,
                "Subtotal" TEXT NOT NULL,
                "DiscountTotal" TEXT NOT NULL,
                "TaxTotal" TEXT NOT NULL,
                "GrandTotal" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "ApprovedAt" TEXT NULL,
                "ClosedAt" TEXT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("SupplierDetailAccountId") REFERENCES detail_accounts ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_purchase_orders_CompanyId_Number"
            ON purchase_orders ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_order_lines (
                "Id" TEXT NOT NULL CONSTRAINT "PK_purchase_order_lines" PRIMARY KEY,
                "PurchaseOrderId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "ReceivedQuantity" TEXT NOT NULL DEFAULT '0',
                "UnitCost" TEXT NOT NULL,
                "DiscountAmount" TEXT NOT NULL,
                "TaxAmount" TEXT NOT NULL,
                "NetAmount" TEXT NOT NULL,
                FOREIGN KEY ("PurchaseOrderId") REFERENCES purchase_orders ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_receipts (
                "Id" TEXT NOT NULL CONSTRAINT "PK_purchase_receipts" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "FiscalYearId" TEXT NOT NULL,
                "Number" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "CurrencyId" TEXT NULL,
                "ExchangeRate" TEXT NOT NULL DEFAULT '1',
                "SupplierDetailAccountId" TEXT NULL,
                "WarehouseId" TEXT NOT NULL,
                "PaymentType" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Description" TEXT NULL,
                "Subtotal" TEXT NOT NULL,
                "DiscountTotal" TEXT NOT NULL,
                "TaxTotal" TEXT NOT NULL,
                "GrandTotal" TEXT NOT NULL,
                "AccountingJournalEntryId" TEXT NULL,
                "ReversalJournalEntryId" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "PostedAt" TEXT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("SupplierDetailAccountId") REFERENCES detail_accounts ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_purchase_receipts_CompanyId_Number"
            ON purchase_receipts ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_receipt_lines (
                "Id" TEXT NOT NULL CONSTRAINT "PK_purchase_receipt_lines" PRIMARY KEY,
                "PurchaseReceiptId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "UnitCost" TEXT NOT NULL,
                "DiscountAmount" TEXT NOT NULL,
                "TaxAmount" TEXT NOT NULL,
                "NetAmount" TEXT NOT NULL,
                "LotNumber" TEXT NULL,
                "SerialNumber" TEXT NULL,
                "ExpiryDate" TEXT NULL,
                FOREIGN KEY ("PurchaseReceiptId") REFERENCES purchase_receipts ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS warehouse_transfers (
                "Id" TEXT NOT NULL CONSTRAINT "PK_warehouse_transfers" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "Number" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "FromWarehouseId" TEXT NOT NULL,
                "ToWarehouseId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Description" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "PostedAt" TEXT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FromWarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ToWarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_warehouse_transfers_CompanyId_Number"
            ON warehouse_transfers ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS warehouse_transfer_lines (
                "Id" TEXT NOT NULL CONSTRAINT "PK_warehouse_transfer_lines" PRIMARY KEY,
                "WarehouseTransferId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "LotNumber" TEXT NULL,
                "SerialNumber" TEXT NULL,
                "ExpiryDate" TEXT NULL,
                FOREIGN KEY ("WarehouseTransferId") REFERENCES warehouse_transfers ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_returns (
                "Id" TEXT NOT NULL CONSTRAINT "PK_sales_returns" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "FiscalYearId" TEXT NOT NULL,
                "SalesInvoiceId" TEXT NOT NULL,
                "Number" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "WarehouseId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Reason" TEXT NULL,
                "GrandTotal" TEXT NOT NULL,
                "TaxTotal" TEXT NOT NULL,
                "CostTotal" TEXT NOT NULL,
                "AccountingJournalEntryId" TEXT NULL,
                "ReversalJournalEntryId" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("SalesInvoiceId") REFERENCES sales_invoices ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_returns_CompanyId_Number"
            ON sales_returns ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_return_lines (
                "Id" TEXT NOT NULL CONSTRAINT "PK_sales_return_lines" PRIMARY KEY,
                "SalesReturnId" TEXT NOT NULL,
                "SalesInvoiceLineId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "NetAmount" TEXT NOT NULL,
                "TaxAmount" TEXT NOT NULL,
                "UnitCost" TEXT NOT NULL,
                "CostAmount" TEXT NOT NULL,
                FOREIGN KEY ("SalesReturnId") REFERENCES sales_returns ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("SalesInvoiceLineId") REFERENCES sales_invoice_lines ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_returns (
                "Id" TEXT NOT NULL CONSTRAINT "PK_purchase_returns" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "FiscalYearId" TEXT NOT NULL,
                "PurchaseReceiptId" TEXT NOT NULL,
                "Number" TEXT NOT NULL,
                "DocumentDate" TEXT NOT NULL,
                "WarehouseId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Reason" TEXT NULL,
                "GrandTotal" TEXT NOT NULL,
                "TaxTotal" TEXT NOT NULL,
                "AccountingJournalEntryId" TEXT NULL,
                "ReversalJournalEntryId" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("PurchaseReceiptId") REFERENCES purchase_receipts ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_purchase_returns_CompanyId_Number"
            ON purchase_returns ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_return_lines (
                "Id" TEXT NOT NULL CONSTRAINT "PK_purchase_return_lines" PRIMARY KEY,
                "PurchaseReturnId" TEXT NOT NULL,
                "PurchaseReceiptLineId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "Quantity" TEXT NOT NULL,
                "NetAmount" TEXT NOT NULL,
                "TaxAmount" TEXT NOT NULL,
                "UnitCost" TEXT NOT NULL,
                FOREIGN KEY ("PurchaseReturnId") REFERENCES purchase_returns ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("PurchaseReceiptLineId") REFERENCES purchase_receipt_lines ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }

        await EnsureSqliteColumnAsync(
            db, "store_products", "TrackingMode",
            "ALTER TABLE store_products ADD COLUMN \"TrackingMode\" TEXT NOT NULL DEFAULT 'None';",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "store_products", "MinimumStock",
            "ALTER TABLE store_products ADD COLUMN \"MinimumStock\" TEXT NOT NULL DEFAULT '0';",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "sales_inventory_settings", "PayablesAccountId",
            "ALTER TABLE sales_inventory_settings ADD COLUMN \"PayablesAccountId\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "sales_inventory_settings", "PurchaseTaxReceivableAccountId",
            "ALTER TABLE sales_inventory_settings ADD COLUMN \"PurchaseTaxReceivableAccountId\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "sales_invoice_lines", "LotNumber",
            "ALTER TABLE sales_invoice_lines ADD COLUMN \"LotNumber\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "sales_invoice_lines", "SerialNumber",
            "ALTER TABLE sales_invoice_lines ADD COLUMN \"SerialNumber\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "sales_invoice_lines", "ExpiryDate",
            "ALTER TABLE sales_invoice_lines ADD COLUMN \"ExpiryDate\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "stock_movements", "LotNumber",
            "ALTER TABLE stock_movements ADD COLUMN \"LotNumber\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "stock_movements", "SerialNumber",
            "ALTER TABLE stock_movements ADD COLUMN \"SerialNumber\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "stock_movements", "ExpiryDate",
            "ALTER TABLE stock_movements ADD COLUMN \"ExpiryDate\" TEXT NULL;",
            cancellationToken);

        await EnsureSqliteColumnAsync(
            db, "sales_invoices", "CurrencyId",
            "ALTER TABLE sales_invoices ADD COLUMN \"CurrencyId\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "sales_invoices", "ExchangeRate",
            "ALTER TABLE sales_invoices ADD COLUMN \"ExchangeRate\" TEXT NOT NULL DEFAULT '1';",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "purchase_receipts", "CurrencyId",
            "ALTER TABLE purchase_receipts ADD COLUMN \"CurrencyId\" TEXT NULL;",
            cancellationToken);
        await EnsureSqliteColumnAsync(
            db, "purchase_receipts", "ExchangeRate",
            "ALTER TABLE purchase_receipts ADD COLUMN \"ExchangeRate\" TEXT NOT NULL DEFAULT '1';",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_stock_movements_Company_Product_Lot\" ON stock_movements (\"CompanyId\", \"ProductId\", \"LotNumber\");",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_stock_movements_Company_Serial\" ON stock_movements (\"CompanyId\", \"SerialNumber\");",
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
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

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
                "CurrencyId" uuid NULL,
                "ExchangeRate" numeric(24,8) NOT NULL DEFAULT 1,
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
            """,
            """
            ALTER TABLE sales_invoices
                ADD COLUMN IF NOT EXISTS "CurrencyId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "ExchangeRate" numeric(24,8) NOT NULL DEFAULT 1;
            """,
            """
            ALTER TABLE purchase_receipts
                ADD COLUMN IF NOT EXISTS "CurrencyId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "ExchangeRate" numeric(24,8) NOT NULL DEFAULT 1;
            """,
            """
            ALTER TABLE store_products
                ADD COLUMN IF NOT EXISTS "TrackingMode" character varying(30) NOT NULL DEFAULT 'None',
                ADD COLUMN IF NOT EXISTS "MinimumStock" numeric(20,4) NOT NULL DEFAULT 0;
            """,
            """
            ALTER TABLE sales_inventory_settings
                ADD COLUMN IF NOT EXISTS "PayablesAccountId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "PurchaseTaxReceivableAccountId" uuid NULL;
            """,
            """
            ALTER TABLE sales_invoice_lines
                ADD COLUMN IF NOT EXISTS "LotNumber" character varying(120) NULL,
                ADD COLUMN IF NOT EXISTS "SerialNumber" character varying(180) NULL,
                ADD COLUMN IF NOT EXISTS "ExpiryDate" date NULL;
            """,
            """
            ALTER TABLE stock_movements
                ADD COLUMN IF NOT EXISTS "LotNumber" character varying(120) NULL,
                ADD COLUMN IF NOT EXISTS "SerialNumber" character varying(180) NULL,
                ADD COLUMN IF NOT EXISTS "ExpiryDate" date NULL;
            """,
            """
            CREATE INDEX IF NOT EXISTS "IX_stock_movements_Company_Product_Lot"
            ON stock_movements ("CompanyId", "ProductId", "LotNumber");
            """,
            """
            CREATE INDEX IF NOT EXISTS "IX_stock_movements_Company_Serial"
            ON stock_movements ("CompanyId", "SerialNumber");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_orders (
                "Id" uuid NOT NULL CONSTRAINT "PK_purchase_orders" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL,
                "Number" character varying(80) NOT NULL,
                "DocumentDate" date NOT NULL,
                "ExpectedDate" date NULL,
                "CurrencyId" uuid NULL,
                "ExchangeRate" numeric(24,8) NOT NULL DEFAULT 1,
                "SupplierDetailAccountId" uuid NOT NULL,
                "WarehouseId" uuid NOT NULL,
                "Status" character varying(30) NOT NULL,
                "Description" character varying(500) NULL,
                "Subtotal" numeric(20,4) NOT NULL,
                "DiscountTotal" numeric(20,4) NOT NULL,
                "TaxTotal" numeric(20,4) NOT NULL,
                "GrandTotal" numeric(20,4) NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ApprovedAt" timestamp with time zone NULL,
                "ClosedAt" timestamp with time zone NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CurrencyId") REFERENCES currencies ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("SupplierDetailAccountId") REFERENCES detail_accounts ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_purchase_orders_CompanyId_Number"
            ON purchase_orders ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_order_lines (
                "Id" uuid NOT NULL CONSTRAINT "PK_purchase_order_lines" PRIMARY KEY,
                "PurchaseOrderId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Quantity" numeric(20,4) NOT NULL,
                "ReceivedQuantity" numeric(20,4) NOT NULL DEFAULT 0,
                "UnitCost" numeric(20,4) NOT NULL,
                "DiscountAmount" numeric(20,4) NOT NULL,
                "TaxAmount" numeric(20,4) NOT NULL,
                "NetAmount" numeric(20,4) NOT NULL,
                FOREIGN KEY ("PurchaseOrderId") REFERENCES purchase_orders ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_receipts (
                "Id" uuid NOT NULL CONSTRAINT "PK_purchase_receipts" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL,
                "Number" character varying(80) NOT NULL,
                "DocumentDate" date NOT NULL,
                "CurrencyId" uuid NULL,
                "ExchangeRate" numeric(24,8) NOT NULL DEFAULT 1,
                "SupplierDetailAccountId" uuid NULL,
                "WarehouseId" uuid NOT NULL,
                "PaymentType" character varying(30) NOT NULL,
                "Status" character varying(30) NOT NULL,
                "Description" character varying(500) NULL,
                "Subtotal" numeric(20,4) NOT NULL,
                "DiscountTotal" numeric(20,4) NOT NULL,
                "TaxTotal" numeric(20,4) NOT NULL,
                "GrandTotal" numeric(20,4) NOT NULL,
                "AccountingJournalEntryId" uuid NULL,
                "ReversalJournalEntryId" uuid NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "PostedAt" timestamp with time zone NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("SupplierDetailAccountId") REFERENCES detail_accounts ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_purchase_receipts_CompanyId_Number"
            ON purchase_receipts ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_receipt_lines (
                "Id" uuid NOT NULL CONSTRAINT "PK_purchase_receipt_lines" PRIMARY KEY,
                "PurchaseReceiptId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Quantity" numeric(20,4) NOT NULL,
                "UnitCost" numeric(20,4) NOT NULL,
                "DiscountAmount" numeric(20,4) NOT NULL,
                "TaxAmount" numeric(20,4) NOT NULL,
                "NetAmount" numeric(20,4) NOT NULL,
                "LotNumber" character varying(120) NULL,
                "SerialNumber" character varying(180) NULL,
                "ExpiryDate" date NULL,
                FOREIGN KEY ("PurchaseReceiptId") REFERENCES purchase_receipts ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS warehouse_transfers (
                "Id" uuid NOT NULL CONSTRAINT "PK_warehouse_transfers" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "Number" character varying(80) NOT NULL,
                "DocumentDate" date NOT NULL,
                "FromWarehouseId" uuid NOT NULL,
                "ToWarehouseId" uuid NOT NULL,
                "Status" character varying(30) NOT NULL,
                "Description" character varying(500) NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "PostedAt" timestamp with time zone NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FromWarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ToWarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_warehouse_transfers_CompanyId_Number"
            ON warehouse_transfers ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS warehouse_transfer_lines (
                "Id" uuid NOT NULL CONSTRAINT "PK_warehouse_transfer_lines" PRIMARY KEY,
                "WarehouseTransferId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Quantity" numeric(20,4) NOT NULL,
                "LotNumber" character varying(120) NULL,
                "SerialNumber" character varying(180) NULL,
                "ExpiryDate" date NULL,
                FOREIGN KEY ("WarehouseTransferId") REFERENCES warehouse_transfers ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_returns (
                "Id" uuid NOT NULL CONSTRAINT "PK_sales_returns" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL,
                "SalesInvoiceId" uuid NOT NULL,
                "Number" character varying(80) NOT NULL,
                "DocumentDate" date NOT NULL,
                "WarehouseId" uuid NOT NULL,
                "Status" character varying(30) NOT NULL,
                "Reason" character varying(500) NULL,
                "GrandTotal" numeric(20,4) NOT NULL,
                "TaxTotal" numeric(20,4) NOT NULL,
                "CostTotal" numeric(20,4) NOT NULL,
                "AccountingJournalEntryId" uuid NULL,
                "ReversalJournalEntryId" uuid NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("SalesInvoiceId") REFERENCES sales_invoices ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_sales_returns_CompanyId_Number"
            ON sales_returns ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS sales_return_lines (
                "Id" uuid NOT NULL CONSTRAINT "PK_sales_return_lines" PRIMARY KEY,
                "SalesReturnId" uuid NOT NULL,
                "SalesInvoiceLineId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Quantity" numeric(20,4) NOT NULL,
                "NetAmount" numeric(20,4) NOT NULL,
                "TaxAmount" numeric(20,4) NOT NULL,
                "UnitCost" numeric(20,4) NOT NULL,
                "CostAmount" numeric(20,4) NOT NULL,
                FOREIGN KEY ("SalesReturnId") REFERENCES sales_returns ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("SalesInvoiceLineId") REFERENCES sales_invoice_lines ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_returns (
                "Id" uuid NOT NULL CONSTRAINT "PK_purchase_returns" PRIMARY KEY,
                "CompanyId" uuid NOT NULL,
                "FiscalYearId" uuid NOT NULL,
                "PurchaseReceiptId" uuid NOT NULL,
                "Number" character varying(80) NOT NULL,
                "DocumentDate" date NOT NULL,
                "WarehouseId" uuid NOT NULL,
                "Status" character varying(30) NOT NULL,
                "Reason" character varying(500) NULL,
                "GrandTotal" numeric(20,4) NOT NULL,
                "TaxTotal" numeric(20,4) NOT NULL,
                "AccountingJournalEntryId" uuid NULL,
                "ReversalJournalEntryId" uuid NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("FiscalYearId") REFERENCES fiscal_years ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("PurchaseReceiptId") REFERENCES purchase_receipts ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("WarehouseId") REFERENCES warehouses ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("AccountingJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ReversalJournalEntryId") REFERENCES journal_entries ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_purchase_returns_CompanyId_Number"
            ON purchase_returns ("CompanyId", "Number");
            """,
            """
            CREATE TABLE IF NOT EXISTS purchase_return_lines (
                "Id" uuid NOT NULL CONSTRAINT "PK_purchase_return_lines" PRIMARY KEY,
                "PurchaseReturnId" uuid NOT NULL,
                "PurchaseReceiptLineId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "Quantity" numeric(20,4) NOT NULL,
                "NetAmount" numeric(20,4) NOT NULL,
                "TaxAmount" numeric(20,4) NOT NULL,
                "UnitCost" numeric(20,4) NOT NULL,
                FOREIGN KEY ("PurchaseReturnId") REFERENCES purchase_returns ("Id") ON DELETE CASCADE,
                FOREIGN KEY ("PurchaseReceiptLineId") REFERENCES purchase_receipt_lines ("Id") ON DELETE RESTRICT,
                FOREIGN KEY ("ProductId") REFERENCES store_products ("Id") ON DELETE RESTRICT
            );
            """
        };

        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command, cancellationToken);
        }
    }
}
