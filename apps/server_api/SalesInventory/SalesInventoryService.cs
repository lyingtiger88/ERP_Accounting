using System.Data;
using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.SalesInventory;

public sealed class SalesInventoryService(
    AppDbContext db,
    AccountingService accountingService)
{
    public async Task EnsureDefaultsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Warehouses.AnyAsync(
                x => x.CompanyId == companyId,
                cancellationToken))
        {
            db.Warehouses.Add(new Warehouse
            {
                CompanyId = companyId,
                Code = "MAIN",
                Name = "انبار اصلی"
            });
        }

        if (!await db.SalesInventorySettings.AnyAsync(
                x => x.CompanyId == companyId,
                cancellationToken))
        {
            var requiredCodes = new[]
            {
                "1110",
                "1200",
                "1300",
                "2200",
                "4100",
                "5100"
            };

            var accounts = await db.Accounts
                .Where(x =>
                    x.CompanyId == companyId &&
                    requiredCodes.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, cancellationToken);

            var missing = requiredCodes
                .Where(code => !accounts.ContainsKey(code))
                .ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Default accounting mappings cannot be created. Missing account codes: " +
                    string.Join(", ", missing));
            }

            db.SalesInventorySettings.Add(new SalesInventorySettings
            {
                CompanyId = companyId,
                CashAccountId = accounts["1110"].Id,
                ReceivablesAccountId = accounts["1200"].Id,
                InventoryAccountId = accounts["1300"].Id,
                SalesTaxPayableAccountId = accounts["2200"].Id,
                SalesRevenueAccountId = accounts["4100"].Id,
                CostOfGoodsSoldAccountId = accounts["5100"].Id,
                PreventNegativeStock = true
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductView>> GetProductsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.StoreProducts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Sku)
            .Select(x => new ProductView(
                x.Id,
                x.Sku,
                x.Name,
                x.Barcode,
                x.UnitName,
                x.Kind,
                x.TrackInventory,
                x.SalesPrice,
                x.DefaultPurchasePrice,
                x.IsActive))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ProductView> CreateProductAsync(
        Guid companyId,
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var sku = request.Sku.Trim();
        var name = request.Name.Trim();
        var unitName = request.UnitName.Trim();
        var barcode = NullIfBlank(request.Barcode);

        ValidateProductFields(
            sku,
            name,
            unitName,
            request.SalesPrice,
            request.DefaultPurchasePrice);

        if (await db.StoreProducts.AnyAsync(
                x => x.CompanyId == companyId && x.Sku == sku,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Product SKU already exists.");
        }

        var product = new StoreProduct
        {
            CompanyId = companyId,
            Sku = sku,
            Name = name,
            Barcode = barcode,
            UnitName = unitName,
            Kind = request.Kind,
            TrackInventory =
                request.Kind == ProductKind.Inventory &&
                request.TrackInventory,
            SalesPrice = request.SalesPrice,
            DefaultPurchasePrice = request.DefaultPurchasePrice
        };

        db.StoreProducts.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return ToProductView(product);
    }

    public async Task<ProductView> UpdateProductAsync(
        Guid companyId,
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await db.StoreProducts
            .FirstOrDefaultAsync(
                x => x.Id == productId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Product does not exist in this company.");

        var sku = request.Sku.Trim();
        var name = request.Name.Trim();
        var unitName = request.UnitName.Trim();
        var barcode = NullIfBlank(request.Barcode);

        ValidateProductFields(
            sku,
            name,
            unitName,
            request.SalesPrice,
            request.DefaultPurchasePrice);

        if (await db.StoreProducts.AnyAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.Id != productId &&
                    x.Sku == sku,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Product SKU already exists.");
        }

        product.Sku = sku;
        product.Name = name;
        product.Barcode = barcode;
        product.UnitName = unitName;
        product.Kind = request.Kind;
        product.TrackInventory =
            request.Kind == ProductKind.Inventory &&
            request.TrackInventory;
        product.SalesPrice = request.SalesPrice;
        product.DefaultPurchasePrice = request.DefaultPurchasePrice;
        product.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
        return ToProductView(product);
    }

    public async Task<IReadOnlyList<WarehouseView>> GetWarehousesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.Warehouses
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .Select(x => new WarehouseView(
                x.Id,
                x.Code,
                x.Name,
                x.IsActive))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<WarehouseView> CreateWarehouseAsync(
        Guid companyId,
        CreateWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Warehouse code and name are required.");
        }

        if (await db.Warehouses.AnyAsync(
                x => x.CompanyId == companyId && x.Code == code,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Warehouse code already exists.");
        }

        var warehouse = new Warehouse
        {
            CompanyId = companyId,
            Code = code,
            Name = name
        };

        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);

        return new WarehouseView(
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            warehouse.IsActive);
    }

    public async Task<WarehouseView> UpdateWarehouseAsync(
        Guid companyId,
        Guid warehouseId,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await db.Warehouses
            .FirstOrDefaultAsync(
                x => x.Id == warehouseId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Warehouse does not exist in this company.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Warehouse code and name are required.");
        }

        if (await db.Warehouses.AnyAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.Id != warehouseId &&
                    x.Code == code,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Warehouse code already exists.");
        }

        warehouse.Code = code;
        warehouse.Name = name;
        warehouse.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return new WarehouseView(
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            warehouse.IsActive);
    }

    public async Task<SalesInventorySettingsView> GetSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(companyId, cancellationToken);

        var settings = await db.SalesInventorySettings
            .AsNoTracking()
            .SingleAsync(
                x => x.CompanyId == companyId,
                cancellationToken);

        return ToSettingsView(settings);
    }

    public async Task<SalesInventorySettingsView> UpdateSettingsAsync(
        Guid companyId,
        UpdateSalesInventorySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateSettingsAccountsAsync(
            companyId,
            request,
            cancellationToken);

        var settings = await db.SalesInventorySettings
            .FirstOrDefaultAsync(
                x => x.CompanyId == companyId,
                cancellationToken);

        if (settings is null)
        {
            settings = new SalesInventorySettings
            {
                CompanyId = companyId,
                ReceivablesAccountId = request.ReceivablesAccountId,
                CashAccountId = request.CashAccountId,
                SalesRevenueAccountId = request.SalesRevenueAccountId,
                InventoryAccountId = request.InventoryAccountId,
                CostOfGoodsSoldAccountId = request.CostOfGoodsSoldAccountId,
                SalesTaxPayableAccountId = request.SalesTaxPayableAccountId,
                PreventNegativeStock = request.PreventNegativeStock
            };

            db.SalesInventorySettings.Add(settings);
        }
        else
        {
            settings.ReceivablesAccountId = request.ReceivablesAccountId;
            settings.CashAccountId = request.CashAccountId;
            settings.SalesRevenueAccountId = request.SalesRevenueAccountId;
            settings.InventoryAccountId = request.InventoryAccountId;
            settings.CostOfGoodsSoldAccountId = request.CostOfGoodsSoldAccountId;
            settings.SalesTaxPayableAccountId = request.SalesTaxPayableAccountId;
            settings.PreventNegativeStock = request.PreventNegativeStock;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToSettingsView(settings);
    }

    public async Task<IReadOnlyList<StockBalanceView>> GetStockBalancesAsync(
        Guid companyId,
        Guid? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                (!warehouseId.HasValue || x.Id == warehouseId.Value))
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                x.TrackInventory)
            .OrderBy(x => x.Sku)
            .ToArrayAsync(cancellationToken);

        var warehouseIds = warehouses.Select(x => x.Id).ToArray();
        var productIds = products.Select(x => x.Id).ToArray();

        var movements = warehouseIds.Length == 0 || productIds.Length == 0
            ? []
            : await db.StockMovements
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    warehouseIds.Contains(x.WarehouseId) &&
                    productIds.Contains(x.ProductId))
                .ToArrayAsync(cancellationToken);

        var rows = new List<StockBalanceView>();

        foreach (var warehouse in warehouses)
        {
            foreach (var product in products)
            {
                var productMovements = movements
                    .Where(x =>
                        x.WarehouseId == warehouse.Id &&
                        x.ProductId == product.Id)
                    .ToArray();

                var quantity = productMovements.Sum(x => x.Quantity);
                var value = productMovements.Sum(
                    x => x.Quantity * x.UnitCost);
                var averageCost = quantity == 0
                    ? 0
                    : value / quantity;

                rows.Add(new StockBalanceView(
                    product.Id,
                    product.Sku,
                    product.Name,
                    warehouse.Id,
                    warehouse.Name,
                    quantity,
                    averageCost,
                    value));
            }
        }

        return rows;
    }

    public async Task<StockBalanceView> AdjustStockAsync(
        Guid companyId,
        Guid userId,
        CreateStockAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.QuantityDelta == 0)
        {
            throw new ArgumentException(
                "Stock adjustment quantity cannot be zero.");
        }

        var reason = request.Reason.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Adjustment reason is required.");
        }

        var warehouse = await db.Warehouses
            .FirstOrDefaultAsync(
                x =>
                    x.Id == request.WarehouseId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Warehouse does not exist or is inactive.");

        var product = await db.StoreProducts
            .FirstOrDefaultAsync(
                x =>
                    x.Id == request.ProductId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Product does not exist or is inactive.");

        if (!product.TrackInventory)
        {
            throw new InvalidOperationException(
                "This product does not track inventory.");
        }

        var balance = await GetStockBalanceCoreAsync(
            companyId,
            warehouse.Id,
            product.Id,
            cancellationToken);

        decimal unitCost;
        StockMovementType movementType;

        if (request.QuantityDelta > 0)
        {
            unitCost = request.UnitCost ??
                product.DefaultPurchasePrice;

            if (unitCost < 0)
            {
                throw new ArgumentException(
                    "Unit cost cannot be negative.");
            }

            movementType = StockMovementType.AdjustmentIn;
        }
        else
        {
            var settings = await GetOrCreateSettingsEntityAsync(
                companyId,
                cancellationToken);

            if (settings.PreventNegativeStock &&
                balance.Quantity + request.QuantityDelta < 0)
            {
                throw new InvalidOperationException(
                    "Adjustment would create negative stock.");
            }

            unitCost = balance.AverageCost;
            movementType = StockMovementType.AdjustmentOut;
        }

        db.StockMovements.Add(new StockMovement
        {
            CompanyId = companyId,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            DocumentDate = request.DocumentDate,
            Type = movementType,
            Quantity = request.QuantityDelta,
            UnitCost = unitCost,
            ReferenceType = "ManualAdjustment",
            Description = reason,
            CreatedByUserId = userId
        });

        await db.SaveChangesAsync(cancellationToken);

        return await GetStockBalanceCoreAsync(
            companyId,
            warehouse.Id,
            product.Id,
            cancellationToken);
    }

    public async Task<SalesInvoiceView> CreateSalesInvoiceAsync(
        Guid companyId,
        Guid userId,
        CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Sales invoice requires at least one line.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == request.FiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal year does not exist in this company.");

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == request.WarehouseId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Warehouse does not exist or is inactive.");

        if (request.PaymentType == SalesPaymentType.Credit &&
            request.CustomerDetailAccountId is null)
        {
            throw new ArgumentException(
                "Customer is required for a credit sale.");
        }

        if (request.CustomerDetailAccountId is Guid customerId)
        {
            var customerExists = await db.DetailAccounts.AnyAsync(
                x =>
                    x.Id == customerId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken);

            if (!customerExists)
            {
                throw new ArgumentException(
                    "Customer detail account does not exist or is inactive.");
            }
        }

        var productIds = request.Lines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();

        var products = await db.StoreProducts
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (products.Count != productIds.Length)
        {
            throw new ArgumentException(
                "One or more invoice products do not exist or are inactive.");
        }

        var invoiceNumber = await GenerateInvoiceNumberAsync(
            companyId,
            fiscalYear.PersianYear,
            request.FiscalYearId,
            cancellationToken);

        var invoice = new SalesInvoice
        {
            CompanyId = companyId,
            FiscalYearId = request.FiscalYearId,
            Number = invoiceNumber,
            DocumentDate = request.DocumentDate,
            CustomerDetailAccountId = request.CustomerDetailAccountId,
            WarehouseId = warehouse.Id,
            PaymentType = request.PaymentType,
            Description = NullIfBlank(request.Description),
            CreatedByUserId = userId
        };

        foreach (var requestedLine in request.Lines)
        {
            if (requestedLine.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Invoice line quantity must be greater than zero.");
            }

            if (requestedLine.DiscountAmount < 0 ||
                requestedLine.TaxAmount < 0)
            {
                throw new ArgumentException(
                    "Discount and tax cannot be negative.");
            }

            var product = products[requestedLine.ProductId];
            var unitPrice = requestedLine.UnitPrice ??
                product.SalesPrice;

            if (unitPrice < 0)
            {
                throw new ArgumentException(
                    "Unit price cannot be negative.");
            }

            var gross = requestedLine.Quantity * unitPrice;

            if (requestedLine.DiscountAmount > gross)
            {
                throw new ArgumentException(
                    "Line discount cannot exceed gross line amount.");
            }

            var net = gross - requestedLine.DiscountAmount;

            invoice.Lines.Add(new SalesInvoiceLine
            {
                SalesInvoiceId = invoice.Id,
                ProductId = product.Id,
                Quantity = requestedLine.Quantity,
                UnitPrice = unitPrice,
                DiscountAmount = requestedLine.DiscountAmount,
                TaxAmount = requestedLine.TaxAmount,
                NetAmount = net,
                UnitCost = 0,
                CostAmount = 0
            });

            invoice.Subtotal += gross;
            invoice.DiscountTotal += requestedLine.DiscountAmount;
            invoice.TaxTotal += requestedLine.TaxAmount;
        }

        invoice.GrandTotal =
            invoice.Subtotal -
            invoice.DiscountTotal +
            invoice.TaxTotal;

        if (invoice.GrandTotal <= 0)
        {
            throw new ArgumentException(
                "Invoice grand total must be greater than zero.");
        }

        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildInvoiceViewAsync(
            invoice.Id,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SalesInvoiceView>> GetSalesInvoicesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var ids = await db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        var result = new List<SalesInvoiceView>(ids.Length);

        foreach (var id in ids)
        {
            result.Add(await BuildInvoiceViewAsync(
                id,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<PostSalesInvoiceResponse> PostSalesInvoiceAsync(
        Guid companyId,
        Guid userId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var invoice = await db.SalesInvoices
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == invoiceId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Sales invoice does not exist in this company.");

        if (invoice.Status != SalesInvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft sales invoices can be posted.");
        }

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == invoice.WarehouseId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Invoice warehouse does not exist or is inactive.");

        var settings = await GetOrCreateSettingsEntityAsync(
            companyId,
            cancellationToken);

        var productIds = invoice.Lines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (products.Count != productIds.Length)
        {
            throw new InvalidOperationException(
                "Invoice references missing products.");
        }

        var inventoryProductIds = products.Values
            .Where(x => x.TrackInventory)
            .Select(x => x.Id)
            .ToArray();

        var existingMovements = inventoryProductIds.Length == 0
            ? []
            : await db.StockMovements
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.WarehouseId == warehouse.Id &&
                    inventoryProductIds.Contains(x.ProductId))
                .ToArrayAsync(cancellationToken);

        var requestedByProduct = invoice.Lines
            .GroupBy(x => x.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(x => x.Quantity));

        var averageCostByProduct = new Dictionary<Guid, decimal>();

        foreach (var productId in inventoryProductIds)
        {
            var productMovements = existingMovements
                .Where(x => x.ProductId == productId)
                .ToArray();

            var currentQuantity = productMovements.Sum(x => x.Quantity);
            var currentValue = productMovements.Sum(
                x => x.Quantity * x.UnitCost);
            var averageCost = currentQuantity == 0
                ? products[productId].DefaultPurchasePrice
                : currentValue / currentQuantity;

            var requestedQuantity =
                requestedByProduct.GetValueOrDefault(productId);

            if (settings.PreventNegativeStock &&
                currentQuantity < requestedQuantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for product '{products[productId].Name}'. " +
                    $"Available: {currentQuantity}, requested: {requestedQuantity}.");
            }

            averageCostByProduct[productId] = averageCost;
        }

        decimal totalCost = 0;

        foreach (var line in invoice.Lines)
        {
            var product = products[line.ProductId];

            if (!product.TrackInventory)
            {
                line.UnitCost = 0;
                line.CostAmount = 0;
                continue;
            }

            var unitCost = averageCostByProduct[line.ProductId];
            var costAmount = unitCost * line.Quantity;

            line.UnitCost = unitCost;
            line.CostAmount = costAmount;
            totalCost += costAmount;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = companyId,
                WarehouseId = warehouse.Id,
                ProductId = product.Id,
                DocumentDate = invoice.DocumentDate,
                Type = StockMovementType.SaleIssue,
                Quantity = -line.Quantity,
                UnitCost = unitCost,
                ReferenceType = "SalesInvoice",
                ReferenceId = invoice.Id,
                Description = $"خروج بابت فاکتور فروش {invoice.Number}",
                CreatedByUserId = userId
            });
        }

        invoice.CostTotal = totalCost;

        var journalLines = new List<CreateJournalLineRequest>();

        var settlementAccountId =
            invoice.PaymentType == SalesPaymentType.Cash
                ? settings.CashAccountId
                : settings.ReceivablesAccountId;

        if (invoice.PaymentType == SalesPaymentType.Credit &&
            invoice.CustomerDetailAccountId is null)
        {
            throw new InvalidOperationException(
                "Credit sale requires a customer detail account.");
        }

        journalLines.Add(new CreateJournalLineRequest(
            settlementAccountId,
            $"فاکتور فروش {invoice.Number}",
            invoice.GrandTotal,
            0,
            invoice.PaymentType == SalesPaymentType.Credit
                ? invoice.CustomerDetailAccountId
                : null));

        var netSales = invoice.Subtotal - invoice.DiscountTotal;

        if (netSales > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.SalesRevenueAccountId,
                $"درآمد فروش فاکتور {invoice.Number}",
                0,
                netSales));
        }

        if (invoice.TaxTotal > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.SalesTaxPayableAccountId,
                $"مالیات و عوارض فاکتور {invoice.Number}",
                0,
                invoice.TaxTotal));
        }

        if (totalCost > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.CostOfGoodsSoldAccountId,
                $"بهای تمام‌شده فاکتور {invoice.Number}",
                totalCost,
                0));

            journalLines.Add(new CreateJournalLineRequest(
                settings.InventoryAccountId,
                $"خروج موجودی فاکتور {invoice.Number}",
                0,
                totalCost));
        }

        var journal = await accountingService
            .PostJournalWithinCurrentTransactionAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    invoice.DocumentDate,
                    $"ثبت حسابداری فاکتور فروش {invoice.Number}",
                    journalLines,
                    invoice.FiscalYearId),
                "POST_SALES_INVOICE",
                cancellationToken);

        invoice.AccountingJournalEntryId = journal.Id;
        invoice.Status = SalesInvoiceStatus.Posted;
        invoice.PostedAt = DateTimeOffset.UtcNow;

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "SalesInvoice",
            EntityId = invoice.Id,
            Action = "SALES_INVOICE_POST",
            PayloadJson =
                $"{{\"invoiceNumber\":\"{invoice.Number}\",\"journalNumber\":\"{journal.Number}\"}}"
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PostSalesInvoiceResponse(
            invoice.Id,
            invoice.Number,
            journal.Id,
            journal.Number,
            invoice.GrandTotal,
            invoice.CostTotal);
    }

    private async Task<SalesInventorySettings> GetOrCreateSettingsEntityAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var settings = await db.SalesInventorySettings
            .FirstOrDefaultAsync(
                x => x.CompanyId == companyId,
                cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        await EnsureDefaultsAsync(companyId, cancellationToken);

        return await db.SalesInventorySettings
            .SingleAsync(
                x => x.CompanyId == companyId,
                cancellationToken);
    }

    private async Task ValidateSettingsAccountsAsync(
        Guid companyId,
        UpdateSalesInventorySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var ids = new[]
        {
            request.ReceivablesAccountId,
            request.CashAccountId,
            request.SalesRevenueAccountId,
            request.InventoryAccountId,
            request.CostOfGoodsSoldAccountId,
            request.SalesTaxPayableAccountId
        };

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (accounts.Count != ids.Distinct().Count())
        {
            throw new ArgumentException(
                "One or more accounting mapping accounts are missing or inactive.");
        }

        ValidateAccountType(
            accounts[request.ReceivablesAccountId],
            AccountType.Asset,
            "Receivables");
        ValidateAccountType(
            accounts[request.CashAccountId],
            AccountType.Asset,
            "Cash");
        ValidateAccountType(
            accounts[request.SalesRevenueAccountId],
            AccountType.Revenue,
            "Sales revenue");
        ValidateAccountType(
            accounts[request.InventoryAccountId],
            AccountType.Asset,
            "Inventory");
        ValidateAccountType(
            accounts[request.CostOfGoodsSoldAccountId],
            AccountType.Expense,
            "Cost of goods sold");
        ValidateAccountType(
            accounts[request.SalesTaxPayableAccountId],
            AccountType.Liability,
            "Sales tax payable");
    }

    private static void ValidateAccountType(
        LedgerAccount account,
        AccountType expected,
        string label)
    {
        if (account.Type != expected)
        {
            throw new ArgumentException(
                $"{label} account must be of type {expected}.");
        }
    }

    private async Task<StockBalanceView> GetStockBalanceCoreAsync(
        Guid companyId,
        Guid warehouseId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == warehouseId &&
                    x.CompanyId == companyId,
                cancellationToken);

        var product = await db.StoreProducts
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == productId &&
                    x.CompanyId == companyId,
                cancellationToken);

        var movements = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.WarehouseId == warehouseId &&
                x.ProductId == productId)
            .ToArrayAsync(cancellationToken);

        var quantity = movements.Sum(x => x.Quantity);
        var value = movements.Sum(x => x.Quantity * x.UnitCost);
        var averageCost = quantity == 0 ? 0 : value / quantity;

        return new StockBalanceView(
            product.Id,
            product.Sku,
            product.Name,
            warehouse.Id,
            warehouse.Name,
            quantity,
            averageCost,
            value);
    }

    private async Task<string> GenerateInvoiceNumberAsync(
        Guid companyId,
        int persianYear,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var count = await db.SalesInvoices.CountAsync(
            x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId,
            cancellationToken);

        return $"INV-{persianYear}-{count + 1:000000}";
    }

    private async Task<SalesInvoiceView> BuildInvoiceViewAsync(
        Guid invoiceId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var invoice = await db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == invoiceId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Sales invoice does not exist in this company.");

        var productIds = invoice.Lines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();

        var products = productIds.Length == 0
            ? new Dictionary<Guid, StoreProduct>()
            : await db.StoreProducts
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    productIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == invoice.WarehouseId &&
                    x.CompanyId == companyId,
                cancellationToken);

        string? customerName = null;

        if (invoice.CustomerDetailAccountId is Guid customerId)
        {
            customerName = await db.DetailAccounts
                .AsNoTracking()
                .Where(x =>
                    x.Id == customerId &&
                    x.CompanyId == companyId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? journalNumber = null;

        if (invoice.AccountingJournalEntryId is Guid journalId)
        {
            journalNumber = await db.JournalEntries
                .AsNoTracking()
                .Where(x => x.Id == journalId)
                .Select(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var lines = invoice.Lines
            .OrderBy(x => x.Id)
            .Select(line =>
            {
                products.TryGetValue(line.ProductId, out var product);

                return new SalesInvoiceLineView(
                    line.Id,
                    line.ProductId,
                    product?.Sku ?? "?",
                    product?.Name ?? "کالای نامشخص",
                    product?.UnitName ?? "عدد",
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TaxAmount,
                    line.NetAmount,
                    line.UnitCost,
                    line.CostAmount);
            })
            .ToArray();

        return new SalesInvoiceView(
            invoice.Id,
            invoice.FiscalYearId,
            invoice.Number,
            invoice.DocumentDate,
            invoice.CustomerDetailAccountId,
            customerName,
            invoice.WarehouseId,
            warehouse.Name,
            invoice.PaymentType,
            invoice.Status,
            invoice.Description,
            invoice.Subtotal,
            invoice.DiscountTotal,
            invoice.TaxTotal,
            invoice.GrandTotal,
            invoice.CostTotal,
            invoice.AccountingJournalEntryId,
            journalNumber,
            invoice.CreatedAt,
            invoice.PostedAt,
            lines);
    }

    private static void ValidateProductFields(
        string sku,
        string name,
        string unitName,
        decimal salesPrice,
        decimal purchasePrice)
    {
        if (string.IsNullOrWhiteSpace(sku) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(unitName))
        {
            throw new ArgumentException(
                "Product SKU, name and unit are required.");
        }

        if (salesPrice < 0 || purchasePrice < 0)
        {
            throw new ArgumentException(
                "Product prices cannot be negative.");
        }
    }

    private static ProductView ToProductView(StoreProduct product)
    {
        return new ProductView(
            product.Id,
            product.Sku,
            product.Name,
            product.Barcode,
            product.UnitName,
            product.Kind,
            product.TrackInventory,
            product.SalesPrice,
            product.DefaultPurchasePrice,
            product.IsActive);
    }

    private static SalesInventorySettingsView ToSettingsView(
        SalesInventorySettings settings)
    {
        return new SalesInventorySettingsView(
            settings.ReceivablesAccountId,
            settings.CashAccountId,
            settings.SalesRevenueAccountId,
            settings.InventoryAccountId,
            settings.CostOfGoodsSoldAccountId,
            settings.SalesTaxPayableAccountId,
            settings.PreventNegativeStock);
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }
}
