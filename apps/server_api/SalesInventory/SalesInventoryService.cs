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
                "1410",
                "2100",
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
                PayablesAccountId = accounts["2100"].Id,
                InventoryAccountId = accounts["1300"].Id,
                PurchaseTaxReceivableAccountId = accounts["1410"].Id,
                SalesTaxPayableAccountId = accounts["2200"].Id,
                SalesRevenueAccountId = accounts["4100"].Id,
                CostOfGoodsSoldAccountId = accounts["5100"].Id,
                PreventNegativeStock = true
            });
        }

        var existingSettings = await db.SalesInventorySettings
            .FirstOrDefaultAsync(
                x => x.CompanyId == companyId,
                cancellationToken);

        if (existingSettings is not null &&
            (!existingSettings.PayablesAccountId.HasValue ||
             !existingSettings.PurchaseTaxReceivableAccountId.HasValue))
        {
            var upgradeCodes = new[] { "2100", "1410" };

            var upgradeAccounts = await db.Accounts
                .Where(x =>
                    x.CompanyId == companyId &&
                    upgradeCodes.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, cancellationToken);

            existingSettings.PayablesAccountId ??=
                upgradeAccounts.GetValueOrDefault("2100")?.Id;
            existingSettings.PurchaseTaxReceivableAccountId ??=
                upgradeAccounts.GetValueOrDefault("1410")?.Id;
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
                x.IsActive,
                x.TrackingMode,
                x.MinimumStock))
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
            request.DefaultPurchasePrice,
            request.MinimumStock);

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
            TrackingMode =
                request.Kind == ProductKind.Inventory && request.TrackInventory
                    ? request.TrackingMode
                    : InventoryTrackingMode.None,
            MinimumStock =
                request.Kind == ProductKind.Inventory && request.TrackInventory
                    ? request.MinimumStock
                    : 0,
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
            request.DefaultPurchasePrice,
            request.MinimumStock);

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
        product.TrackingMode =
            product.TrackInventory
                ? request.TrackingMode
                : InventoryTrackingMode.None;
        product.MinimumStock =
            product.TrackInventory
                ? request.MinimumStock
                : 0;
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
                PayablesAccountId = request.PayablesAccountId,
                CashAccountId = request.CashAccountId,
                SalesRevenueAccountId = request.SalesRevenueAccountId,
                InventoryAccountId = request.InventoryAccountId,
                CostOfGoodsSoldAccountId = request.CostOfGoodsSoldAccountId,
                SalesTaxPayableAccountId = request.SalesTaxPayableAccountId,
                PurchaseTaxReceivableAccountId =
                    request.PurchaseTaxReceivableAccountId,
                PreventNegativeStock = request.PreventNegativeStock
            };

            db.SalesInventorySettings.Add(settings);
        }
        else
        {
            settings.ReceivablesAccountId = request.ReceivablesAccountId;
            if (request.PayablesAccountId.HasValue)
            {
                settings.PayablesAccountId = request.PayablesAccountId;
            }
            settings.CashAccountId = request.CashAccountId;
            settings.SalesRevenueAccountId = request.SalesRevenueAccountId;
            settings.InventoryAccountId = request.InventoryAccountId;
            settings.CostOfGoodsSoldAccountId = request.CostOfGoodsSoldAccountId;
            settings.SalesTaxPayableAccountId = request.SalesTaxPayableAccountId;
            if (request.PurchaseTaxReceivableAccountId.HasValue)
            {
                settings.PurchaseTaxReceivableAccountId =
                    request.PurchaseTaxReceivableAccountId;
            }
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
                    value,
                    product.MinimumStock,
                    product.MinimumStock > 0 && quantity <= product.MinimumStock));
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
            LotNumber = NullIfBlank(request.LotNumber),
            SerialNumber = NullIfBlank(request.SerialNumber),
            ExpiryDate = request.ExpiryDate,
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

        var tradeCurrency = await ResolveTradeCurrencyAsync(
            companyId,
            request.CurrencyId,
            request.ExchangeRate,
            request.DocumentDate,
            cancellationToken);

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
            CurrencyId = tradeCurrency.CurrencyId,
            ExchangeRate = tradeCurrency.ExchangeRate,
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

            ValidateTraceFields(
                product,
                requestedLine.Quantity,
                requestedLine.LotNumber,
                requestedLine.SerialNumber);

            var unitPrice = requestedLine.UnitPrice ??
                (tradeCurrency.CurrencyId.HasValue
                    ? ConvertFromBase(
                        product.SalesPrice,
                        tradeCurrency.ExchangeRate)
                    : product.SalesPrice);

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
                CostAmount = 0,
                LotNumber = NullIfBlank(requestedLine.LotNumber),
                SerialNumber = NullIfBlank(requestedLine.SerialNumber),
                ExpiryDate = requestedLine.ExpiryDate
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
        var invoiceKeys = await db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.DocumentDate,
                x.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var ids = invoiceKeys
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();

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

        var reservedByTrace = new Dictionary<
            (Guid ProductId, string? Lot, string? Serial, DateOnly? Expiry),
            decimal>();

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

            ValidateTraceFields(
                product,
                line.Quantity,
                line.LotNumber,
                line.SerialNumber);

            var relevantMovements = existingMovements
                .Where(x => x.ProductId == product.Id)
                .ToArray();

            if (product.TrackingMode == InventoryTrackingMode.Serial)
            {
                relevantMovements = relevantMovements
                    .Where(x => x.SerialNumber == line.SerialNumber)
                    .ToArray();
            }
            else if (product.TrackingMode == InventoryTrackingMode.Lot)
            {
                relevantMovements = relevantMovements
                    .Where(x =>
                        x.LotNumber == line.LotNumber &&
                        x.ExpiryDate == line.ExpiryDate)
                    .ToArray();
            }

            var currentQuantity = relevantMovements.Sum(x => x.Quantity);
            var currentValue = relevantMovements.Sum(
                x => x.Quantity * x.UnitCost);
            var unitCost = currentQuantity == 0
                ? product.DefaultPurchasePrice
                : currentValue / currentQuantity;

            var traceKey = (
                product.Id,
                product.TrackingMode == InventoryTrackingMode.Lot
                    ? line.LotNumber
                    : null,
                product.TrackingMode == InventoryTrackingMode.Serial
                    ? line.SerialNumber
                    : null,
                product.TrackingMode == InventoryTrackingMode.Lot
                    ? line.ExpiryDate
                    : null);

            var reserved =
                reservedByTrace.GetValueOrDefault(traceKey);
            var required = reserved + line.Quantity;

            if (settings.PreventNegativeStock &&
                currentQuantity < required)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for product '{product.Name}'. " +
                    $"Available: {currentQuantity}, requested: {required}.");
            }

            reservedByTrace[traceKey] = required;

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
                LotNumber = line.LotNumber,
                SerialNumber = line.SerialNumber,
                ExpiryDate = line.ExpiryDate,
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

        var rate = invoice.ExchangeRate <= 0
            ? 1m
            : invoice.ExchangeRate;

        var settlementBase = ConvertToBase(
            invoice.GrandTotal,
            rate);

        journalLines.Add(new CreateJournalLineRequest(
            settlementAccountId,
            $"فاکتور فروش {invoice.Number}",
            settlementBase,
            0,
            invoice.PaymentType == SalesPaymentType.Credit
                ? invoice.CustomerDetailAccountId
                : null,
            CurrencyId: invoice.CurrencyId,
            ForeignDebit: invoice.CurrencyId.HasValue
                ? invoice.GrandTotal
                : null,
            ForeignCredit: invoice.CurrencyId.HasValue
                ? 0m
                : null,
            ExchangeRate: invoice.CurrencyId.HasValue
                ? rate
                : null));

        var netSales = invoice.Subtotal - invoice.DiscountTotal;

        if (netSales > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.SalesRevenueAccountId,
                $"درآمد فروش فاکتور {invoice.Number}",
                0,
                ConvertToBase(netSales, rate)));
        }

        if (invoice.TaxTotal > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.SalesTaxPayableAccountId,
                $"مالیات و عوارض فاکتور {invoice.Number}",
                0,
                ConvertToBase(invoice.TaxTotal, rate)));
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

    public async Task<ProductView?> FindProductAsync(
        Guid companyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim();

        if (normalized.Length == 0)
        {
            return null;
        }

        var product = await db.StoreProducts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.IsActive &&
                    (x.Sku == normalized || x.Barcode == normalized),
                cancellationToken);

        return product is null ? null : ToProductView(product);
    }

    public async Task<IReadOnlyList<LowStockAlertView>> GetLowStockAlertsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var balances = await GetStockBalancesAsync(
            companyId,
            null,
            cancellationToken);

        return balances
            .Where(x => x.IsLowStock)
            .OrderByDescending(x => Math.Max(0, x.MinimumStock - x.Quantity))
            .Select(x => new LowStockAlertView(
                x.ProductId,
                x.Sku,
                x.ProductName,
                x.WarehouseId,
                x.WarehouseName,
                x.Quantity,
                x.MinimumStock,
                Math.Max(0, x.MinimumStock - x.Quantity)))
            .ToArray();
    }

    public async Task<IReadOnlyList<StockTraceBalanceView>> GetStockTraceBalancesAsync(
        Guid companyId,
        Guid? warehouseId = null,
        Guid? productId = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.StockMovements
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (warehouseId is Guid selectedWarehouse)
        {
            query = query.Where(x => x.WarehouseId == selectedWarehouse);
        }

        if (productId is Guid selectedProduct)
        {
            query = query.Where(x => x.ProductId == selectedProduct);
        }

        var movements = await query.ToArrayAsync(cancellationToken);

        var warehouseIds = movements.Select(x => x.WarehouseId).Distinct().ToArray();
        var productIds = movements.Select(x => x.ProductId).Distinct().ToArray();

        var warehouses = warehouseIds.Length == 0
            ? new Dictionary<Guid, Warehouse>()
            : await db.Warehouses
                .AsNoTracking()
                .Where(x => warehouseIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var products = productIds.Length == 0
            ? new Dictionary<Guid, StoreProduct>()
            : await db.StoreProducts
                .AsNoTracking()
                .Where(x => productIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        return movements
            .GroupBy(x => new
            {
                x.WarehouseId,
                x.ProductId,
                x.LotNumber,
                x.SerialNumber,
                x.ExpiryDate
            })
            .Select(group =>
            {
                var quantity = group.Sum(x => x.Quantity);
                var value = group.Sum(x => x.Quantity * x.UnitCost);
                var averageCost = quantity == 0 ? 0 : value / quantity;

                products.TryGetValue(group.Key.ProductId, out var product);
                warehouses.TryGetValue(group.Key.WarehouseId, out var warehouse);

                return new StockTraceBalanceView(
                    group.Key.ProductId,
                    product?.Sku ?? "?",
                    product?.Name ?? "کالای نامشخص",
                    group.Key.WarehouseId,
                    warehouse?.Name ?? "انبار نامشخص",
                    group.Key.LotNumber,
                    group.Key.SerialNumber,
                    group.Key.ExpiryDate,
                    quantity,
                    averageCost);
            })
            .Where(x => x.Quantity != 0)
            .OrderBy(x => x.Sku)
            .ThenBy(x => x.ExpiryDate)
            .ToArray();
    }

    public async Task<PurchaseOrderView> CreatePurchaseOrderAsync(
        Guid companyId,
        Guid userId,
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Purchase order requires at least one line.");
        }

        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == request.FiscalYearId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal year does not exist in this company.");

        var warehouseExists = await db.Warehouses.AnyAsync(
            x =>
                x.Id == request.WarehouseId &&
                x.CompanyId == companyId &&
                x.IsActive,
            cancellationToken);

        if (!warehouseExists)
        {
            throw new ArgumentException(
                "Warehouse does not exist or is inactive.");
        }

        var supplierExists = await db.DetailAccounts.AnyAsync(
            x =>
                x.Id == request.SupplierDetailAccountId &&
                x.CompanyId == companyId &&
                x.IsActive,
            cancellationToken);

        if (!supplierExists)
        {
            throw new ArgumentException(
                "Supplier detail account does not exist or is inactive.");
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
                "Purchase order contains missing or inactive products.");
        }

        var tradeCurrency = await ResolveTradeCurrencyAsync(
            companyId,
            request.CurrencyId,
            request.ExchangeRate,
            request.DocumentDate,
            cancellationToken);

        var order = new PurchaseOrder
        {
            CompanyId = companyId,
            FiscalYearId = fiscalYear.Id,
            Number = await GeneratePurchaseOrderNumberAsync(
                companyId,
                fiscalYear.PersianYear,
                fiscalYear.Id,
                cancellationToken),
            DocumentDate = request.DocumentDate,
            ExpectedDate = request.ExpectedDate,
            CurrencyId = tradeCurrency.CurrencyId,
            ExchangeRate = tradeCurrency.ExchangeRate,
            SupplierDetailAccountId = request.SupplierDetailAccountId,
            WarehouseId = request.WarehouseId,
            Description = NullIfBlank(request.Description),
            CreatedByUserId = userId
        };

        foreach (var requestedLine in request.Lines)
        {
            if (requestedLine.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Purchase order quantity must be greater than zero.");
            }

            if (requestedLine.DiscountAmount < 0 ||
                requestedLine.TaxAmount < 0)
            {
                throw new ArgumentException(
                    "Purchase order amounts cannot be negative.");
            }

            var product = products[requestedLine.ProductId];
            var unitCost = requestedLine.UnitCost ??
                (tradeCurrency.CurrencyId.HasValue
                    ? ConvertFromBase(
                        product.DefaultPurchasePrice,
                        tradeCurrency.ExchangeRate)
                    : product.DefaultPurchasePrice);

            if (unitCost < 0)
            {
                throw new ArgumentException(
                    "Purchase order unit cost cannot be negative.");
            }

            var gross = requestedLine.Quantity * unitCost;
            if (requestedLine.DiscountAmount > gross)
            {
                throw new ArgumentException(
                    "Purchase order discount cannot exceed gross amount.");
            }

            var net = gross - requestedLine.DiscountAmount;

            order.Lines.Add(new PurchaseOrderLine
            {
                PurchaseOrderId = order.Id,
                ProductId = product.Id,
                Quantity = requestedLine.Quantity,
                ReceivedQuantity = 0,
                UnitCost = unitCost,
                DiscountAmount = requestedLine.DiscountAmount,
                TaxAmount = requestedLine.TaxAmount,
                NetAmount = net
            });

            order.Subtotal += gross;
            order.DiscountTotal += requestedLine.DiscountAmount;
            order.TaxTotal += requestedLine.TaxAmount;
        }

        order.GrandTotal =
            order.Subtotal - order.DiscountTotal + order.TaxTotal;

        if (order.GrandTotal <= 0)
        {
            throw new ArgumentException(
                "Purchase order grand total must be greater than zero.");
        }

        db.PurchaseOrders.Add(order);

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "PurchaseOrder",
            EntityId = order.Id,
            Action = "PURCHASE_ORDER_CREATE",
            PayloadJson =
                $@"{{""orderNumber"":""{order.Number}""}}"
        });

        await db.SaveChangesAsync(cancellationToken);

        return await BuildPurchaseOrderViewAsync(
            order.Id,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseOrderView>> GetPurchaseOrdersAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var keys = await db.PurchaseOrders
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.DocumentDate,
                x.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var ids = keys
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();

        var result = new List<PurchaseOrderView>(ids.Length);
        foreach (var id in ids)
        {
            result.Add(await BuildPurchaseOrderViewAsync(
                id,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<PurchaseOrderView> SetPurchaseOrderStatusAsync(
        Guid companyId,
        Guid userId,
        Guid orderId,
        PurchaseOrderStatus targetStatus,
        CancellationToken cancellationToken = default)
    {
        if (targetStatus == PurchaseOrderStatus.Draft)
        {
            throw new ArgumentException(
                "Purchase order cannot be moved back to draft.");
        }

        var order = await db.PurchaseOrders
            .FirstOrDefaultAsync(
                x => x.Id == orderId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Purchase order does not exist in this company.");

        var validTransition =
            (order.Status == PurchaseOrderStatus.Draft &&
             targetStatus is PurchaseOrderStatus.Approved or
                 PurchaseOrderStatus.Cancelled) ||
            (order.Status == PurchaseOrderStatus.Approved &&
             targetStatus is PurchaseOrderStatus.Closed or
                 PurchaseOrderStatus.Cancelled);

        if (!validTransition)
        {
            throw new InvalidOperationException(
                $"Cannot change purchase order from {order.Status} to {targetStatus}.");
        }

        order.Status = targetStatus;
        if (targetStatus == PurchaseOrderStatus.Approved)
        {
            order.ApprovedAt = DateTimeOffset.UtcNow;
        }
        if (targetStatus is PurchaseOrderStatus.Closed or
            PurchaseOrderStatus.Cancelled)
        {
            order.ClosedAt = DateTimeOffset.UtcNow;
        }

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "PurchaseOrder",
            EntityId = order.Id,
            Action = "PURCHASE_ORDER_STATUS",
            PayloadJson =
                $@"{{""orderNumber"":""{order.Number}"",""status"":""{targetStatus}""}}"
        });

        await db.SaveChangesAsync(cancellationToken);

        return await BuildPurchaseOrderViewAsync(
            order.Id,
            companyId,
            cancellationToken);
    }

    public async Task<PurchaseReceiptView> CreatePurchaseReceiptAsync(
        Guid companyId,
        Guid userId,
        CreatePurchaseReceiptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Purchase receipt requires at least one line.");
        }

        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == request.FiscalYearId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal year does not exist in this company.");

        var warehouseExists = await db.Warehouses.AnyAsync(
            x =>
                x.Id == request.WarehouseId &&
                x.CompanyId == companyId &&
                x.IsActive,
            cancellationToken);

        if (!warehouseExists)
        {
            throw new ArgumentException(
                "Warehouse does not exist or is inactive.");
        }

        if (request.PaymentType == PurchasePaymentType.Credit &&
            request.SupplierDetailAccountId is null)
        {
            throw new ArgumentException(
                "Supplier is required for a credit purchase.");
        }

        if (request.SupplierDetailAccountId is Guid supplierId)
        {
            var supplierExists = await db.DetailAccounts.AnyAsync(
                x =>
                    x.Id == supplierId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken);

            if (!supplierExists)
            {
                throw new ArgumentException(
                    "Supplier detail account does not exist or is inactive.");
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
                x.TrackInventory &&
                productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (products.Count != productIds.Length)
        {
            throw new ArgumentException(
                "Purchase receipt only accepts active inventory-tracked products.");
        }

        var tradeCurrency = await ResolveTradeCurrencyAsync(
            companyId,
            request.CurrencyId,
            request.ExchangeRate,
            request.DocumentDate,
            cancellationToken);

        var receipt = new PurchaseReceipt
        {
            CompanyId = companyId,
            FiscalYearId = fiscalYear.Id,
            Number = await GeneratePurchaseNumberAsync(
                companyId,
                fiscalYear.PersianYear,
                fiscalYear.Id,
                cancellationToken),
            DocumentDate = request.DocumentDate,
            CurrencyId = tradeCurrency.CurrencyId,
            ExchangeRate = tradeCurrency.ExchangeRate,
            SupplierDetailAccountId = request.SupplierDetailAccountId,
            WarehouseId = request.WarehouseId,
            PaymentType = request.PaymentType,
            Description = NullIfBlank(request.Description),
            CreatedByUserId = userId
        };

        foreach (var requestedLine in request.Lines)
        {
            if (requestedLine.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Purchase quantity must be greater than zero.");
            }

            var product = products[requestedLine.ProductId];
            ValidateTraceFields(
                product,
                requestedLine.Quantity,
                requestedLine.LotNumber,
                requestedLine.SerialNumber);

            var unitCost = requestedLine.UnitCost ??
                (tradeCurrency.CurrencyId.HasValue
                    ? ConvertFromBase(
                        product.DefaultPurchasePrice,
                        tradeCurrency.ExchangeRate)
                    : product.DefaultPurchasePrice);

            if (unitCost < 0 ||
                requestedLine.DiscountAmount < 0 ||
                requestedLine.TaxAmount < 0)
            {
                throw new ArgumentException(
                    "Purchase amounts cannot be negative.");
            }

            var gross = requestedLine.Quantity * unitCost;

            if (requestedLine.DiscountAmount > gross)
            {
                throw new ArgumentException(
                    "Purchase discount cannot exceed gross line amount.");
            }

            var net = gross - requestedLine.DiscountAmount;

            receipt.Lines.Add(new PurchaseReceiptLine
            {
                PurchaseReceiptId = receipt.Id,
                ProductId = product.Id,
                Quantity = requestedLine.Quantity,
                UnitCost = unitCost,
                DiscountAmount = requestedLine.DiscountAmount,
                TaxAmount = requestedLine.TaxAmount,
                NetAmount = net,
                LotNumber = NullIfBlank(requestedLine.LotNumber),
                SerialNumber = NullIfBlank(requestedLine.SerialNumber),
                ExpiryDate = requestedLine.ExpiryDate
            });

            receipt.Subtotal += gross;
            receipt.DiscountTotal += requestedLine.DiscountAmount;
            receipt.TaxTotal += requestedLine.TaxAmount;
        }

        receipt.GrandTotal =
            receipt.Subtotal -
            receipt.DiscountTotal +
            receipt.TaxTotal;

        if (receipt.GrandTotal <= 0)
        {
            throw new ArgumentException(
                "Purchase grand total must be greater than zero.");
        }

        db.PurchaseReceipts.Add(receipt);
        await db.SaveChangesAsync(cancellationToken);

        return await BuildPurchaseReceiptViewAsync(
            receipt.Id,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseReceiptView>> GetPurchaseReceiptsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var purchaseKeys = await db.PurchaseReceipts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.DocumentDate,
                x.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var ids = purchaseKeys
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();

        var result = new List<PurchaseReceiptView>(ids.Length);

        foreach (var id in ids)
        {
            result.Add(await BuildPurchaseReceiptViewAsync(
                id,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<PostPurchaseReceiptResponse> PostPurchaseReceiptAsync(
        Guid companyId,
        Guid userId,
        Guid receiptId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var receipt = await db.PurchaseReceipts
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == receiptId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Purchase receipt does not exist in this company.");

        if (receipt.Status != PurchaseReceiptStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft purchase receipts can be posted.");
        }

        var settings = await GetOrCreateSettingsEntityAsync(
            companyId,
            cancellationToken);

        if (settings.PayablesAccountId is not Guid payablesAccountId ||
            settings.PurchaseTaxReceivableAccountId is not Guid purchaseTaxAccountId)
        {
            throw new InvalidOperationException(
                "Purchase accounting mappings are incomplete.");
        }

        var products = await db.StoreProducts
            .Where(x =>
                x.CompanyId == companyId &&
                receipt.Lines.Select(l => l.ProductId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var line in receipt.Lines)
        {
            var product = products[line.ProductId];

            ValidateTraceFields(
                product,
                line.Quantity,
                line.LotNumber,
                line.SerialNumber);

            if (product.TrackingMode == InventoryTrackingMode.Serial &&
                line.SerialNumber is not null)
            {
                var existingSerialQty = await db.StockMovements
                    .AsNoTracking()
                    .Where(x =>
                        x.CompanyId == companyId &&
                        x.ProductId == product.Id &&
                        x.SerialNumber == line.SerialNumber)
                    .SumAsync(x => x.Quantity, cancellationToken);

                if (existingSerialQty > 0)
                {
                    throw new InvalidOperationException(
                        $"Serial '{line.SerialNumber}' already exists in stock.");
                }
            }

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = companyId,
                WarehouseId = receipt.WarehouseId,
                ProductId = product.Id,
                DocumentDate = receipt.DocumentDate,
                Type = StockMovementType.PurchaseReceipt,
                Quantity = line.Quantity,
                UnitCost = ConvertToBase(
                    line.NetAmount,
                    receipt.ExchangeRate) / line.Quantity,
                LotNumber = line.LotNumber,
                SerialNumber = line.SerialNumber,
                ExpiryDate = line.ExpiryDate,
                ReferenceType = "PurchaseReceipt",
                ReferenceId = receipt.Id,
                Description = $"ورود خرید {receipt.Number}",
                CreatedByUserId = userId
            });

            product.DefaultPurchasePrice =
                ConvertToBase(
                    line.NetAmount,
                    receipt.ExchangeRate) / line.Quantity;
        }

        var inventoryNet = receipt.Subtotal - receipt.DiscountTotal;
        var settlementAccount =
            receipt.PaymentType == PurchasePaymentType.Cash
                ? settings.CashAccountId
                : payablesAccountId;
        var rate = receipt.ExchangeRate <= 0
            ? 1m
            : receipt.ExchangeRate;

        var journalLines = new List<CreateJournalLineRequest>
        {
            new(
                settings.InventoryAccountId,
                $"خرید کالا {receipt.Number}",
                ConvertToBase(inventoryNet, rate),
                0)
        };

        if (receipt.TaxTotal > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                purchaseTaxAccountId,
                $"مالیات خرید {receipt.Number}",
                ConvertToBase(receipt.TaxTotal, rate),
                0));
        }

        journalLines.Add(new CreateJournalLineRequest(
            settlementAccount,
            $"تسویه خرید {receipt.Number}",
            0,
            ConvertToBase(receipt.GrandTotal, rate),
            receipt.PaymentType == PurchasePaymentType.Credit
                ? receipt.SupplierDetailAccountId
                : null,
            CurrencyId: receipt.CurrencyId,
            ForeignDebit: receipt.CurrencyId.HasValue
                ? 0m
                : null,
            ForeignCredit: receipt.CurrencyId.HasValue
                ? receipt.GrandTotal
                : null,
            ExchangeRate: receipt.CurrencyId.HasValue
                ? rate
                : null));

        var journal = await accountingService
            .PostJournalWithinCurrentTransactionAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    receipt.DocumentDate,
                    $"ثبت حسابداری خرید {receipt.Number}",
                    journalLines,
                    receipt.FiscalYearId),
                "POST_PURCHASE_RECEIPT",
                cancellationToken);

        receipt.AccountingJournalEntryId = journal.Id;
        receipt.Status = PurchaseReceiptStatus.Posted;
        receipt.PostedAt = DateTimeOffset.UtcNow;

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "PurchaseReceipt",
            EntityId = receipt.Id,
            Action = "PURCHASE_RECEIPT_POST",
            PayloadJson =
                $"{{\"receiptNumber\":\"{receipt.Number}\",\"journalNumber\":\"{journal.Number}\"}}"
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PostPurchaseReceiptResponse(
            receipt.Id,
            receipt.Number,
            journal.Id,
            journal.Number,
            receipt.GrandTotal);
    }

    public async Task<PurchaseReturnView> CreatePurchaseReturnAsync(
        Guid companyId,
        Guid userId,
        Guid receiptId,
        CreatePurchaseReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Purchase return requires at least one line.");
        }

        var reason = request.Reason.Trim();
        if (reason.Length == 0)
        {
            throw new ArgumentException(
                "Purchase return reason is required.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var receipt = await db.PurchaseReceipts
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == receiptId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Purchase receipt does not exist in this company.");

        if (receipt.Status is not PurchaseReceiptStatus.Posted and
            not PurchaseReceiptStatus.Reversed)
        {
            throw new InvalidOperationException(
                "Only posted purchase receipts can be returned.");
        }

        await EnsureOperationalDateOpenAsync(
            companyId,
            request.DocumentDate,
            cancellationToken);

        var requestedLineIds = request.Lines
            .Select(x => x.PurchaseReceiptLineId)
            .Distinct()
            .ToArray();

        var originalLines = receipt.Lines
            .Where(x => requestedLineIds.Contains(x.Id))
            .ToDictionary(x => x.Id);

        if (originalLines.Count != requestedLineIds.Length)
        {
            throw new ArgumentException(
                "Return contains lines that do not belong to the purchase receipt.");
        }

        var previousReturned = await db.PurchaseReturnLines
            .AsNoTracking()
            .Where(x =>
                requestedLineIds.Contains(x.PurchaseReceiptLineId) &&
                db.PurchaseReturns.Any(r =>
                    r.Id == x.PurchaseReturnId &&
                    r.CompanyId == companyId &&
                    r.Status == PurchaseReturnStatus.Posted))
            .GroupBy(x => x.PurchaseReceiptLineId)
            .Select(group => new
            {
                LineId = group.Key,
                Quantity = group.Sum(x => x.Quantity)
            })
            .ToDictionaryAsync(x => x.LineId, x => x.Quantity, cancellationToken);

        var settings = await GetOrCreateSettingsEntityAsync(
            companyId,
            cancellationToken);

        if (settings.PayablesAccountId is not Guid payablesAccountId ||
            settings.PurchaseTaxReceivableAccountId is not Guid purchaseTaxAccountId)
        {
            throw new InvalidOperationException(
                "Purchase accounting mappings are incomplete.");
        }

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                receipt.Lines.Select(l => l.ProductId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var purchaseReturn = new PurchaseReturn
        {
            CompanyId = companyId,
            FiscalYearId = receipt.FiscalYearId,
            PurchaseReceiptId = receipt.Id,
            Number = await GeneratePurchaseReturnNumberAsync(
                companyId,
                receipt.FiscalYearId,
                cancellationToken),
            DocumentDate = request.DocumentDate,
            WarehouseId = receipt.WarehouseId,
            Reason = reason,
            CreatedByUserId = userId
        };

        var rate = receipt.ExchangeRate <= 0 ? 1m : receipt.ExchangeRate;

        foreach (var requested in request.Lines)
        {
            if (requested.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Return quantity must be greater than zero.");
            }

            var original = originalLines[requested.PurchaseReceiptLineId];
            var alreadyReturned =
                previousReturned.GetValueOrDefault(original.Id);

            if (alreadyReturned + requested.Quantity > original.Quantity)
            {
                throw new InvalidOperationException(
                    "Returned quantity cannot exceed the remaining purchased quantity.");
            }

            var product = products[original.ProductId];
            var traceMovements = await GetTraceMovementsAsync(
                companyId,
                receipt.WarehouseId,
                product.Id,
                original.LotNumber,
                original.SerialNumber,
                original.ExpiryDate,
                cancellationToken);

            var available = traceMovements.Sum(x => x.Quantity);
            if (settings.PreventNegativeStock &&
                available < requested.Quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock to return product '{product.Name}'. " +
                    $"Available: {available}, requested: {requested.Quantity}.");
            }

            var ratio = requested.Quantity / original.Quantity;
            var netAmount = original.NetAmount * ratio;
            var taxAmount = original.TaxAmount * ratio;
            var baseUnitCost =
                ConvertToBase(original.NetAmount, rate) / original.Quantity;

            purchaseReturn.Lines.Add(new PurchaseReturnLine
            {
                PurchaseReturnId = purchaseReturn.Id,
                PurchaseReceiptLineId = original.Id,
                ProductId = original.ProductId,
                Quantity = requested.Quantity,
                NetAmount = netAmount,
                TaxAmount = taxAmount,
                UnitCost = baseUnitCost
            });

            purchaseReturn.GrandTotal += netAmount + taxAmount;
            purchaseReturn.TaxTotal += taxAmount;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = companyId,
                WarehouseId = receipt.WarehouseId,
                ProductId = original.ProductId,
                DocumentDate = request.DocumentDate,
                Type = StockMovementType.PurchaseReturn,
                Quantity = -requested.Quantity,
                UnitCost = baseUnitCost,
                LotNumber = original.LotNumber,
                SerialNumber = original.SerialNumber,
                ExpiryDate = original.ExpiryDate,
                ReferenceType = "PurchaseReturn",
                ReferenceId = purchaseReturn.Id,
                Description = $"برگشت از خرید {receipt.Number}",
                CreatedByUserId = userId
            });
        }

        if (purchaseReturn.GrandTotal <= 0)
        {
            throw new InvalidOperationException(
                "Purchase return total must be greater than zero.");
        }

        var settlementAccount =
            receipt.PaymentType == PurchasePaymentType.Cash
                ? settings.CashAccountId
                : payablesAccountId;
        var netInventory =
            purchaseReturn.GrandTotal - purchaseReturn.TaxTotal;

        var journalLines = new List<CreateJournalLineRequest>
        {
            new(
                settlementAccount,
                $"تسویه برگشت از خرید {purchaseReturn.Number}",
                ConvertToBase(purchaseReturn.GrandTotal, rate),
                0,
                receipt.PaymentType == PurchasePaymentType.Credit
                    ? receipt.SupplierDetailAccountId
                    : null,
                CurrencyId: receipt.CurrencyId,
                ForeignDebit: receipt.CurrencyId.HasValue
                    ? purchaseReturn.GrandTotal
                    : null,
                ForeignCredit: receipt.CurrencyId.HasValue
                    ? 0m
                    : null,
                ExchangeRate: receipt.CurrencyId.HasValue
                    ? rate
                    : null),
            new(
                settings.InventoryAccountId,
                $"خروج موجودی برگشت خرید {purchaseReturn.Number}",
                0,
                ConvertToBase(netInventory, rate))
        };

        if (purchaseReturn.TaxTotal > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                purchaseTaxAccountId,
                $"برگشت مالیات خرید {purchaseReturn.Number}",
                0,
                ConvertToBase(purchaseReturn.TaxTotal, rate)));
        }

        var journal = await accountingService
            .PostJournalWithinCurrentTransactionAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    request.DocumentDate,
                    $"ثبت حسابداری برگشت از خرید {purchaseReturn.Number}",
                    journalLines,
                    receipt.FiscalYearId),
                "POST_PURCHASE_RETURN",
                cancellationToken);

        purchaseReturn.AccountingJournalEntryId = journal.Id;
        db.PurchaseReturns.Add(purchaseReturn);

        var allReceiptLineIds = receipt.Lines.Select(x => x.Id).ToArray();
        var allPreviousReturns = await db.PurchaseReturnLines
            .AsNoTracking()
            .Where(x =>
                allReceiptLineIds.Contains(x.PurchaseReceiptLineId) &&
                db.PurchaseReturns.Any(r =>
                    r.Id == x.PurchaseReturnId &&
                    r.CompanyId == companyId &&
                    r.Status == PurchaseReturnStatus.Posted))
            .GroupBy(x => x.PurchaseReceiptLineId)
            .Select(group => new
            {
                LineId = group.Key,
                Quantity = group.Sum(x => x.Quantity)
            })
            .ToDictionaryAsync(x => x.LineId, x => x.Quantity, cancellationToken);

        var newReturnByLine = purchaseReturn.Lines
            .GroupBy(x => x.PurchaseReceiptLineId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));

        var fullyReturned = receipt.Lines.All(line =>
            allPreviousReturns.GetValueOrDefault(line.Id) +
            newReturnByLine.GetValueOrDefault(line.Id) >= line.Quantity);

        if (fullyReturned)
        {
            receipt.Status = PurchaseReceiptStatus.Reversed;
            receipt.ReversalJournalEntryId = journal.Id;
        }

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "PurchaseReturn",
            EntityId = purchaseReturn.Id,
            Action = "PURCHASE_RETURN_POST",
            Reason = reason,
            PayloadJson =
                $"{{\"returnNumber\":\"{purchaseReturn.Number}\",\"receiptNumber\":\"{receipt.Number}\",\"journalNumber\":\"{journal.Number}\"}}"
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildPurchaseReturnViewAsync(
            purchaseReturn.Id,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseReturnView>> GetPurchaseReturnsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var keys = await db.PurchaseReturns
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.DocumentDate,
                x.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var ids = keys
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();

        var result = new List<PurchaseReturnView>(ids.Length);
        foreach (var id in ids)
        {
            result.Add(await BuildPurchaseReturnViewAsync(
                id,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<WarehouseTransferView> CreateWarehouseTransferAsync(
        Guid companyId,
        Guid userId,
        CreateWarehouseTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FromWarehouseId == request.ToWarehouseId)
        {
            throw new ArgumentException(
                "Source and destination warehouses must be different.");
        }

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Warehouse transfer requires at least one line.");
        }

        var warehouseIds = new[]
        {
            request.FromWarehouseId,
            request.ToWarehouseId
        };

        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                warehouseIds.Contains(x.Id))
            .ToArrayAsync(cancellationToken);

        if (warehouses.Length != 2)
        {
            throw new ArgumentException(
                "Source or destination warehouse is missing or inactive.");
        }

        var productIds = request.Lines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                x.TrackInventory &&
                productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (products.Count != productIds.Length)
        {
            throw new ArgumentException(
                "Transfer contains invalid inventory products.");
        }

        var transfer = new WarehouseTransfer
        {
            CompanyId = companyId,
            Number = await GenerateTransferNumberAsync(
                companyId,
                cancellationToken),
            DocumentDate = request.DocumentDate,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            Description = NullIfBlank(request.Description),
            CreatedByUserId = userId
        };

        foreach (var requestedLine in request.Lines)
        {
            if (requestedLine.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Transfer quantity must be greater than zero.");
            }

            var product = products[requestedLine.ProductId];
            ValidateTraceFields(
                product,
                requestedLine.Quantity,
                requestedLine.LotNumber,
                requestedLine.SerialNumber);

            transfer.Lines.Add(new WarehouseTransferLine
            {
                WarehouseTransferId = transfer.Id,
                ProductId = product.Id,
                Quantity = requestedLine.Quantity,
                LotNumber = NullIfBlank(requestedLine.LotNumber),
                SerialNumber = NullIfBlank(requestedLine.SerialNumber),
                ExpiryDate = requestedLine.ExpiryDate
            });
        }

        db.WarehouseTransfers.Add(transfer);
        await db.SaveChangesAsync(cancellationToken);

        return await BuildTransferViewAsync(
            transfer.Id,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<WarehouseTransferView>> GetWarehouseTransfersAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var transferKeys = await db.WarehouseTransfers
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.DocumentDate,
                x.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var ids = transferKeys
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();

        var result = new List<WarehouseTransferView>(ids.Length);

        foreach (var id in ids)
        {
            result.Add(await BuildTransferViewAsync(
                id,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<WarehouseTransferView> PostWarehouseTransferAsync(
        Guid companyId,
        Guid userId,
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var transfer = await db.WarehouseTransfers
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == transferId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Warehouse transfer does not exist in this company.");

        if (transfer.Status != WarehouseTransferStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft warehouse transfers can be posted.");
        }

        await EnsureOperationalDateOpenAsync(
            companyId,
            transfer.DocumentDate,
            cancellationToken);

        var settings = await GetOrCreateSettingsEntityAsync(
            companyId,
            cancellationToken);

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                transfer.Lines.Select(l => l.ProductId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var line in transfer.Lines)
        {
            var product = products[line.ProductId];

            var traceMovements = await GetTraceMovementsAsync(
                companyId,
                transfer.FromWarehouseId,
                product.Id,
                line.LotNumber,
                line.SerialNumber,
                line.ExpiryDate,
                cancellationToken);

            var available = traceMovements.Sum(x => x.Quantity);
            var value = traceMovements.Sum(x => x.Quantity * x.UnitCost);
            var averageCost = available == 0
                ? product.DefaultPurchasePrice
                : value / available;

            if (settings.PreventNegativeStock &&
                available < line.Quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for transfer of '{product.Name}'.");
            }

            db.StockMovements.AddRange(
                new StockMovement
                {
                    CompanyId = companyId,
                    WarehouseId = transfer.FromWarehouseId,
                    ProductId = product.Id,
                    DocumentDate = transfer.DocumentDate,
                    Type = StockMovementType.TransferOut,
                    Quantity = -line.Quantity,
                    UnitCost = averageCost,
                    LotNumber = line.LotNumber,
                    SerialNumber = line.SerialNumber,
                    ExpiryDate = line.ExpiryDate,
                    ReferenceType = "WarehouseTransfer",
                    ReferenceId = transfer.Id,
                    Description = $"خروج انتقال {transfer.Number}",
                    CreatedByUserId = userId
                },
                new StockMovement
                {
                    CompanyId = companyId,
                    WarehouseId = transfer.ToWarehouseId,
                    ProductId = product.Id,
                    DocumentDate = transfer.DocumentDate,
                    Type = StockMovementType.TransferIn,
                    Quantity = line.Quantity,
                    UnitCost = averageCost,
                    LotNumber = line.LotNumber,
                    SerialNumber = line.SerialNumber,
                    ExpiryDate = line.ExpiryDate,
                    ReferenceType = "WarehouseTransfer",
                    ReferenceId = transfer.Id,
                    Description = $"ورود انتقال {transfer.Number}",
                    CreatedByUserId = userId
                });
        }

        transfer.Status = WarehouseTransferStatus.Posted;
        transfer.PostedAt = DateTimeOffset.UtcNow;

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "WarehouseTransfer",
            EntityId = transfer.Id,
            Action = "WAREHOUSE_TRANSFER_POST",
            PayloadJson = $"{{\"transferNumber\":\"{transfer.Number}\"}}"
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildTransferViewAsync(
            transfer.Id,
            companyId,
            cancellationToken);
    }

    public async Task<SalesReturnView> CreateSalesReturnAsync(
        Guid companyId,
        Guid userId,
        Guid invoiceId,
        CreateSalesReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Sales return requires at least one line.");
        }

        var reason = request.Reason.Trim();

        if (reason.Length == 0)
        {
            throw new ArgumentException(
                "Sales return reason is required.");
        }

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

        if (invoice.Status is not SalesInvoiceStatus.Posted and
            not SalesInvoiceStatus.Reversed)
        {
            throw new InvalidOperationException(
                "Only posted sales invoices can be returned.");
        }

        var requestedLineIds = request.Lines
            .Select(x => x.SalesInvoiceLineId)
            .Distinct()
            .ToArray();

        var originalLines = invoice.Lines
            .Where(x => requestedLineIds.Contains(x.Id))
            .ToDictionary(x => x.Id);

        if (originalLines.Count != requestedLineIds.Length)
        {
            throw new ArgumentException(
                "Return contains lines that do not belong to the sales invoice.");
        }

        var previousReturned = await db.SalesReturnLines
            .AsNoTracking()
            .Where(x =>
                requestedLineIds.Contains(x.SalesInvoiceLineId) &&
                db.SalesReturns.Any(r =>
                    r.Id == x.SalesReturnId &&
                    r.CompanyId == companyId &&
                    r.Status == SalesReturnStatus.Posted))
            .GroupBy(x => x.SalesInvoiceLineId)
            .Select(group => new
            {
                LineId = group.Key,
                Quantity = group.Sum(x => x.Quantity)
            })
            .ToDictionaryAsync(x => x.LineId, x => x.Quantity, cancellationToken);

        var salesReturn = new SalesReturn
        {
            CompanyId = companyId,
            FiscalYearId = invoice.FiscalYearId,
            SalesInvoiceId = invoice.Id,
            Number = await GenerateReturnNumberAsync(
                companyId,
                invoice.FiscalYearId,
                cancellationToken),
            DocumentDate = request.DocumentDate,
            WarehouseId = invoice.WarehouseId,
            Reason = reason,
            CreatedByUserId = userId
        };

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                invoice.Lines.Select(l => l.ProductId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var requested in request.Lines)
        {
            if (requested.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Return quantity must be greater than zero.");
            }

            var original = originalLines[requested.SalesInvoiceLineId];
            var alreadyReturned = previousReturned.GetValueOrDefault(original.Id);

            if (alreadyReturned + requested.Quantity > original.Quantity)
            {
                throw new InvalidOperationException(
                    "Returned quantity cannot exceed the remaining invoice quantity.");
            }

            var ratio = requested.Quantity / original.Quantity;
            var netAmount = original.NetAmount * ratio;
            var taxAmount = original.TaxAmount * ratio;
            var costAmount = original.CostAmount * ratio;

            salesReturn.Lines.Add(new SalesReturnLine
            {
                SalesReturnId = salesReturn.Id,
                SalesInvoiceLineId = original.Id,
                ProductId = original.ProductId,
                Quantity = requested.Quantity,
                NetAmount = netAmount,
                TaxAmount = taxAmount,
                UnitCost = original.UnitCost,
                CostAmount = costAmount
            });

            salesReturn.GrandTotal += netAmount + taxAmount;
            salesReturn.TaxTotal += taxAmount;
            salesReturn.CostTotal += costAmount;

            var product = products[original.ProductId];

            if (product.TrackInventory)
            {
                db.StockMovements.Add(new StockMovement
                {
                    CompanyId = companyId,
                    WarehouseId = invoice.WarehouseId,
                    ProductId = original.ProductId,
                    DocumentDate = request.DocumentDate,
                    Type = StockMovementType.SaleReturn,
                    Quantity = requested.Quantity,
                    UnitCost = original.UnitCost,
                    LotNumber = original.LotNumber,
                    SerialNumber = original.SerialNumber,
                    ExpiryDate = original.ExpiryDate,
                    ReferenceType = "SalesReturn",
                    ReferenceId = salesReturn.Id,
                    Description = $"برگشت از فروش {invoice.Number}",
                    CreatedByUserId = userId
                });
            }
        }

        var settings = await GetOrCreateSettingsEntityAsync(
            companyId,
            cancellationToken);

        var settlementAccount =
            invoice.PaymentType == SalesPaymentType.Cash
                ? settings.CashAccountId
                : settings.ReceivablesAccountId;

        var journalLines = new List<CreateJournalLineRequest>();

        var netSales = salesReturn.GrandTotal - salesReturn.TaxTotal;
        var rate = invoice.ExchangeRate <= 0
            ? 1m
            : invoice.ExchangeRate;

        if (netSales > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.SalesRevenueAccountId,
                $"برگشت درآمد فروش {salesReturn.Number}",
                ConvertToBase(netSales, rate),
                0));
        }

        if (salesReturn.TaxTotal > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.SalesTaxPayableAccountId,
                $"برگشت مالیات فروش {salesReturn.Number}",
                ConvertToBase(salesReturn.TaxTotal, rate),
                0));
        }

        journalLines.Add(new CreateJournalLineRequest(
            settlementAccount,
            $"تسویه برگشت از فروش {salesReturn.Number}",
            0,
            ConvertToBase(salesReturn.GrandTotal, rate),
            invoice.PaymentType == SalesPaymentType.Credit
                ? invoice.CustomerDetailAccountId
                : null,
            CurrencyId: invoice.CurrencyId,
            ForeignDebit: invoice.CurrencyId.HasValue
                ? 0m
                : null,
            ForeignCredit: invoice.CurrencyId.HasValue
                ? salesReturn.GrandTotal
                : null,
            ExchangeRate: invoice.CurrencyId.HasValue
                ? rate
                : null));

        if (salesReturn.CostTotal > 0)
        {
            journalLines.Add(new CreateJournalLineRequest(
                settings.InventoryAccountId,
                $"برگشت موجودی فروش {salesReturn.Number}",
                salesReturn.CostTotal,
                0));

            journalLines.Add(new CreateJournalLineRequest(
                settings.CostOfGoodsSoldAccountId,
                $"برگشت بهای تمام‌شده {salesReturn.Number}",
                0,
                salesReturn.CostTotal));
        }

        var journal = await accountingService
            .PostJournalWithinCurrentTransactionAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    request.DocumentDate,
                    $"ثبت حسابداری برگشت از فروش {salesReturn.Number}",
                    journalLines,
                    invoice.FiscalYearId),
                "POST_SALES_RETURN",
                cancellationToken);

        salesReturn.AccountingJournalEntryId = journal.Id;
        db.SalesReturns.Add(salesReturn);

        var allInvoiceLineIds = invoice.Lines.Select(x => x.Id).ToArray();
        var allPreviousReturns = await db.SalesReturnLines
            .AsNoTracking()
            .Where(x =>
                allInvoiceLineIds.Contains(x.SalesInvoiceLineId) &&
                db.SalesReturns.Any(r =>
                    r.Id == x.SalesReturnId &&
                    r.CompanyId == companyId &&
                    r.Status == SalesReturnStatus.Posted))
            .GroupBy(x => x.SalesInvoiceLineId)
            .Select(group => new
            {
                LineId = group.Key,
                Quantity = group.Sum(x => x.Quantity)
            })
            .ToDictionaryAsync(x => x.LineId, x => x.Quantity, cancellationToken);

        var newReturnByLine = salesReturn.Lines
            .GroupBy(x => x.SalesInvoiceLineId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));

        var fullyReturned = invoice.Lines.All(line =>
            allPreviousReturns.GetValueOrDefault(line.Id) +
            newReturnByLine.GetValueOrDefault(line.Id) >= line.Quantity);

        if (fullyReturned)
        {
            invoice.Status = SalesInvoiceStatus.Reversed;
            invoice.ReversalJournalEntryId = journal.Id;
        }

        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "SalesReturn",
            EntityId = salesReturn.Id,
            Action = "SALES_RETURN_POST",
            Reason = reason,
            PayloadJson =
                $"{{\"returnNumber\":\"{salesReturn.Number}\",\"invoiceNumber\":\"{invoice.Number}\",\"journalNumber\":\"{journal.Number}\"}}"
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildSalesReturnViewAsync(
            salesReturn.Id,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SalesReturnView>> GetSalesReturnsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var returnKeys = await db.SalesReturns
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.DocumentDate,
                x.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var ids = returnKeys
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();

        var result = new List<SalesReturnView>(ids.Length);

        foreach (var id in ids)
        {
            result.Add(await BuildSalesReturnViewAsync(
                id,
                companyId,
                cancellationToken));
        }

        return result;
    }

    private async Task<SalesInventorySettings> GetOrCreateSettingsEntityAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var settings = await db.SalesInventorySettings
            .FirstOrDefaultAsync(
                x => x.CompanyId == companyId,
                cancellationToken);

        if (settings is not null &&
            settings.PayablesAccountId.HasValue &&
            settings.PurchaseTaxReceivableAccountId.HasValue)
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
        var ids = new List<Guid>
        {
            request.ReceivablesAccountId,
            request.CashAccountId,
            request.SalesRevenueAccountId,
            request.InventoryAccountId,
            request.CostOfGoodsSoldAccountId,
            request.SalesTaxPayableAccountId
        };

        if (request.PayablesAccountId is Guid payablesId)
        {
            ids.Add(payablesId);
        }

        if (request.PurchaseTaxReceivableAccountId is Guid purchaseTaxId)
        {
            ids.Add(purchaseTaxId);
        }

        var distinctIds = ids.Distinct().ToArray();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                distinctIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (accounts.Count != distinctIds.Length)
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

        if (request.PayablesAccountId is Guid payables)
        {
            ValidateAccountType(
                accounts[payables],
                AccountType.Liability,
                "Payables");
        }

        if (request.PurchaseTaxReceivableAccountId is Guid purchaseTax)
        {
            ValidateAccountType(
                accounts[purchaseTax],
                AccountType.Asset,
                "Purchase tax receivable");
        }
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
            value,
            product.MinimumStock,
            product.MinimumStock > 0 && quantity <= product.MinimumStock);
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

        string? currencyCode = null;
        if (invoice.CurrencyId is Guid invoiceCurrencyId)
        {
            currencyCode = await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.Id == invoiceCurrencyId &&
                    x.CompanyId == companyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken);
        }

        currencyCode ??= await GetBaseCurrencyCodeAsync(
            companyId,
            cancellationToken);

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
                    line.CostAmount,
                    line.LotNumber,
                    line.SerialNumber,
                    line.ExpiryDate);
            })
            .ToArray();

        return new SalesInvoiceView(
            invoice.Id,
            invoice.FiscalYearId,
            invoice.Number,
            invoice.DocumentDate,
            invoice.CurrencyId,
            currencyCode,
            invoice.ExchangeRate,
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

    private static void ValidateTraceFields(
        StoreProduct product,
        decimal quantity,
        string? lotNumber,
        string? serialNumber)
    {
        if (!product.TrackInventory ||
            product.TrackingMode == InventoryTrackingMode.None)
        {
            return;
        }

        if (product.TrackingMode == InventoryTrackingMode.Lot &&
            string.IsNullOrWhiteSpace(lotNumber))
        {
            throw new ArgumentException(
                $"Lot number is required for product '{product.Name}'.");
        }

        if (product.TrackingMode == InventoryTrackingMode.Serial)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new ArgumentException(
                    $"Serial number is required for product '{product.Name}'.");
            }

            if (quantity != 1)
            {
                throw new ArgumentException(
                    $"Serial-tracked product '{product.Name}' must have quantity 1 per line.");
            }
        }
    }

    private async Task<StockMovement[]> GetTraceMovementsAsync(
        Guid companyId,
        Guid warehouseId,
        Guid productId,
        string? lotNumber,
        string? serialNumber,
        DateOnly? expiryDate,
        CancellationToken cancellationToken)
    {
        var query = db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.WarehouseId == warehouseId &&
                x.ProductId == productId);

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            query = query.Where(x => x.SerialNumber == serialNumber);
        }
        else if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            query = query.Where(x =>
                x.LotNumber == lotNumber &&
                x.ExpiryDate == expiryDate);
        }

        return await query.ToArrayAsync(cancellationToken);
    }

    private async Task EnsureOperationalDateOpenAsync(
        Guid companyId,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.StartDate <= documentDate &&
                x.EndDate >= documentDate)
            .OrderByDescending(x => x.IsDefault)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No fiscal year covers the document date.");

        if (fiscalYear.IsClosed)
        {
            throw new InvalidOperationException(
                "Fiscal year is closed.");
        }

        var periods = await db.FiscalPeriods
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYear.Id)
            .ToArrayAsync(cancellationToken);

        if (periods.Length == 0)
        {
            return;
        }

        var period = periods.FirstOrDefault(
            x =>
                x.StartDate <= documentDate &&
                x.EndDate >= documentDate);

        if (period is null)
        {
            throw new InvalidOperationException(
                "No fiscal period covers the document date.");
        }

        if (period.IsClosed)
        {
            throw new InvalidOperationException(
                $"Fiscal period '{period.Name}' is closed.");
        }
    }

    private async Task<string> GeneratePurchaseOrderNumberAsync(
        Guid companyId,
        int persianYear,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var count = await db.PurchaseOrders.CountAsync(
            x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId,
            cancellationToken);

        return $"PO-{persianYear}-{count + 1:000000}";
    }

    private async Task<string> GeneratePurchaseReturnNumberAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken);

        var count = await db.PurchaseReturns.CountAsync(
            x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId,
            cancellationToken);

        return $"PRT-{fiscalYear.PersianYear}-{count + 1:000000}";
    }

    private async Task<string> GeneratePurchaseNumberAsync(
        Guid companyId,
        int persianYear,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var count = await db.PurchaseReceipts.CountAsync(
            x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId,
            cancellationToken);

        return $"PUR-{persianYear}-{count + 1:000000}";
    }

    private async Task<string> GenerateTransferNumberAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var count = await db.WarehouseTransfers.CountAsync(
            x => x.CompanyId == companyId,
            cancellationToken);

        return $"TRF-{count + 1:000000}";
    }

    private async Task<string> GenerateReturnNumberAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken);

        var count = await db.SalesReturns.CountAsync(
            x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId,
            cancellationToken);

        return $"RET-{fiscalYear.PersianYear}-{count + 1:000000}";
    }

    private async Task<PurchaseReceiptView> BuildPurchaseReceiptViewAsync(
        Guid receiptId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var receipt = await db.PurchaseReceipts
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == receiptId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Purchase receipt does not exist in this company.");

        var productIds = receipt.Lines.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouseName = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Id == receipt.WarehouseId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);

        string? supplierName = null;
        if (receipt.SupplierDetailAccountId is Guid supplierId)
        {
            supplierName = await db.DetailAccounts
                .AsNoTracking()
                .Where(x => x.Id == supplierId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? journalNumber = null;
        if (receipt.AccountingJournalEntryId is Guid journalId)
        {
            journalNumber = await db.JournalEntries
                .AsNoTracking()
                .Where(x => x.Id == journalId)
                .Select(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? currencyCode = null;
        if (receipt.CurrencyId is Guid receiptCurrencyId)
        {
            currencyCode = await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.Id == receiptCurrencyId &&
                    x.CompanyId == companyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken);
        }

        currencyCode ??= await GetBaseCurrencyCodeAsync(
            companyId,
            cancellationToken);

        var lines = receipt.Lines
            .Select(line =>
            {
                products.TryGetValue(line.ProductId, out var product);

                return new PurchaseReceiptLineView(
                    line.Id,
                    line.ProductId,
                    product?.Sku ?? "?",
                    product?.Name ?? "کالای نامشخص",
                    product?.UnitName ?? "عدد",
                    line.Quantity,
                    line.UnitCost,
                    line.DiscountAmount,
                    line.TaxAmount,
                    line.NetAmount,
                    line.LotNumber,
                    line.SerialNumber,
                    line.ExpiryDate);
            })
            .ToArray();

        return new PurchaseReceiptView(
            receipt.Id,
            receipt.FiscalYearId,
            receipt.Number,
            receipt.DocumentDate,
            receipt.CurrencyId,
            currencyCode,
            receipt.ExchangeRate,
            receipt.SupplierDetailAccountId,
            supplierName,
            receipt.WarehouseId,
            warehouseName,
            receipt.PaymentType,
            receipt.Status,
            receipt.Description,
            receipt.Subtotal,
            receipt.DiscountTotal,
            receipt.TaxTotal,
            receipt.GrandTotal,
            receipt.AccountingJournalEntryId,
            journalNumber,
            receipt.CreatedAt,
            receipt.PostedAt,
            lines);
    }

    private async Task<PurchaseOrderView> BuildPurchaseOrderViewAsync(
        Guid orderId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == orderId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Purchase order does not exist in this company.");

        var productIds = order.Lines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();
        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var supplierName = await db.DetailAccounts
            .AsNoTracking()
            .Where(x => x.Id == order.SupplierDetailAccountId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);

        var warehouseName = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Id == order.WarehouseId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);

        string? currencyCode = null;
        if (order.CurrencyId is Guid currencyId)
        {
            currencyCode = await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.Id == currencyId &&
                    x.CompanyId == companyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken);
        }

        currencyCode ??= await GetBaseCurrencyCodeAsync(
            companyId,
            cancellationToken);

        var lines = order.Lines.Select(line =>
        {
            products.TryGetValue(line.ProductId, out var product);

            return new PurchaseOrderLineView(
                line.Id,
                line.ProductId,
                product?.Sku ?? "?",
                product?.Name ?? "کالای نامشخص",
                product?.UnitName ?? "عدد",
                line.Quantity,
                line.ReceivedQuantity,
                line.UnitCost,
                line.DiscountAmount,
                line.TaxAmount,
                line.NetAmount);
        }).ToArray();

        return new PurchaseOrderView(
            order.Id,
            order.FiscalYearId,
            order.Number,
            order.DocumentDate,
            order.ExpectedDate,
            order.CurrencyId,
            currencyCode,
            order.ExchangeRate,
            order.SupplierDetailAccountId,
            supplierName,
            order.WarehouseId,
            warehouseName,
            order.Status,
            order.Description,
            order.Subtotal,
            order.DiscountTotal,
            order.TaxTotal,
            order.GrandTotal,
            order.CreatedAt,
            order.ApprovedAt,
            order.ClosedAt,
            lines);
    }

    private async Task<PurchaseReturnView> BuildPurchaseReturnViewAsync(
        Guid returnId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var purchaseReturn = await db.PurchaseReturns
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == returnId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Purchase return does not exist in this company.");

        var receipt = await db.PurchaseReceipts
            .AsNoTracking()
            .Where(x =>
                x.Id == purchaseReturn.PurchaseReceiptId &&
                x.CompanyId == companyId)
            .Select(x => new
            {
                x.Number,
                x.CurrencyId,
                x.ExchangeRate
            })
            .SingleAsync(cancellationToken);

        var productIds = purchaseReturn.Lines
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();
        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouseName = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Id == purchaseReturn.WarehouseId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);

        string? journalNumber = null;
        if (purchaseReturn.AccountingJournalEntryId is Guid journalId)
        {
            journalNumber = await db.JournalEntries
                .AsNoTracking()
                .Where(x => x.Id == journalId)
                .Select(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? currencyCode = null;
        if (receipt.CurrencyId is Guid currencyId)
        {
            currencyCode = await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.Id == currencyId &&
                    x.CompanyId == companyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken);
        }

        currencyCode ??= await GetBaseCurrencyCodeAsync(
            companyId,
            cancellationToken);

        var lines = purchaseReturn.Lines.Select(line =>
        {
            products.TryGetValue(line.ProductId, out var product);

            return new PurchaseReturnLineView(
                line.Id,
                line.PurchaseReceiptLineId,
                line.ProductId,
                product?.Sku ?? "?",
                product?.Name ?? "کالای نامشخص",
                line.Quantity,
                line.NetAmount,
                line.TaxAmount,
                line.UnitCost);
        }).ToArray();

        return new PurchaseReturnView(
            purchaseReturn.Id,
            purchaseReturn.PurchaseReceiptId,
            receipt.Number,
            purchaseReturn.Number,
            purchaseReturn.DocumentDate,
            receipt.CurrencyId,
            currencyCode,
            receipt.ExchangeRate,
            purchaseReturn.WarehouseId,
            warehouseName,
            purchaseReturn.Status,
            purchaseReturn.Reason,
            purchaseReturn.GrandTotal,
            purchaseReturn.TaxTotal,
            purchaseReturn.AccountingJournalEntryId,
            journalNumber,
            purchaseReturn.CreatedAt,
            lines);
    }

    private async Task<WarehouseTransferView> BuildTransferViewAsync(
        Guid transferId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var transfer = await db.WarehouseTransfers
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == transferId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Warehouse transfer does not exist in this company.");

        var warehouseIds = new[]
        {
            transfer.FromWarehouseId,
            transfer.ToWarehouseId
        };

        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Where(x => warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var productIds = transfer.Lines.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var movementCosts = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ReferenceType == "WarehouseTransfer" &&
                x.ReferenceId == transfer.Id &&
                x.Type == StockMovementType.TransferOut)
            .ToArrayAsync(cancellationToken);

        var lines = transfer.Lines.Select(line =>
        {
            products.TryGetValue(line.ProductId, out var product);

            var related = movementCosts
                .Where(x =>
                    x.ProductId == line.ProductId &&
                    x.LotNumber == line.LotNumber &&
                    x.SerialNumber == line.SerialNumber &&
                    x.ExpiryDate == line.ExpiryDate)
                .ToArray();

            var quantity = related.Sum(x => -x.Quantity);
            var cost = quantity == 0
                ? 0
                : related.Sum(x => -x.Quantity * x.UnitCost) / quantity;

            return new WarehouseTransferLineView(
                line.Id,
                line.ProductId,
                product?.Sku ?? "?",
                product?.Name ?? "کالای نامشخص",
                line.Quantity,
                cost,
                line.LotNumber,
                line.SerialNumber,
                line.ExpiryDate);
        }).ToArray();

        return new WarehouseTransferView(
            transfer.Id,
            transfer.Number,
            transfer.DocumentDate,
            transfer.FromWarehouseId,
            warehouses.GetValueOrDefault(transfer.FromWarehouseId)?.Name ?? "?",
            transfer.ToWarehouseId,
            warehouses.GetValueOrDefault(transfer.ToWarehouseId)?.Name ?? "?",
            transfer.Status,
            transfer.Description,
            transfer.CreatedAt,
            transfer.PostedAt,
            lines);
    }

    private async Task<SalesReturnView> BuildSalesReturnViewAsync(
        Guid returnId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var salesReturn = await db.SalesReturns
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.Id == returnId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Sales return does not exist in this company.");

        var productIds = salesReturn.Lines.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouseName = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Id == salesReturn.WarehouseId)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);

        string? journalNumber = null;
        if (salesReturn.AccountingJournalEntryId is Guid journalId)
        {
            journalNumber = await db.JournalEntries
                .AsNoTracking()
                .Where(x => x.Id == journalId)
                .Select(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var lines = salesReturn.Lines.Select(line =>
        {
            products.TryGetValue(line.ProductId, out var product);

            return new SalesReturnLineView(
                line.Id,
                line.SalesInvoiceLineId,
                line.ProductId,
                product?.Sku ?? "?",
                product?.Name ?? "کالای نامشخص",
                line.Quantity,
                line.NetAmount,
                line.TaxAmount,
                line.UnitCost,
                line.CostAmount);
        }).ToArray();

        var sourceInvoice = await db.SalesInvoices
            .AsNoTracking()
            .Where(x =>
                x.Id == salesReturn.SalesInvoiceId &&
                x.CompanyId == companyId)
            .Select(x => new
            {
                x.CurrencyId,
                x.ExchangeRate
            })
            .SingleAsync(cancellationToken);

        string? returnCurrencyCode = null;
        if (sourceInvoice.CurrencyId is Guid returnCurrencyId)
        {
            returnCurrencyCode = await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.Id == returnCurrencyId &&
                    x.CompanyId == companyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken);
        }

        returnCurrencyCode ??= await GetBaseCurrencyCodeAsync(
            companyId,
            cancellationToken);

        return new SalesReturnView(
            salesReturn.Id,
            salesReturn.SalesInvoiceId,
            salesReturn.Number,
            salesReturn.DocumentDate,
            sourceInvoice.CurrencyId,
            returnCurrencyCode,
            sourceInvoice.ExchangeRate,
            salesReturn.WarehouseId,
            warehouseName,
            salesReturn.Status,
            salesReturn.Reason,
            salesReturn.GrandTotal,
            salesReturn.TaxTotal,
            salesReturn.CostTotal,
            salesReturn.AccountingJournalEntryId,
            journalNumber,
            salesReturn.CreatedAt,
            lines);
    }

    private static void ValidateProductFields(
        string sku,
        string name,
        string unitName,
        decimal salesPrice,
        decimal purchasePrice,
        decimal minimumStock)
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

        if (minimumStock < 0)
        {
            throw new ArgumentException(
                "Minimum stock cannot be negative.");
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
            product.IsActive,
            product.TrackingMode,
            product.MinimumStock);
    }

    private static SalesInventorySettingsView ToSettingsView(
        SalesInventorySettings settings)
    {
        return new SalesInventorySettingsView(
            settings.ReceivablesAccountId,
            settings.PayablesAccountId,
            settings.CashAccountId,
            settings.SalesRevenueAccountId,
            settings.InventoryAccountId,
            settings.CostOfGoodsSoldAccountId,
            settings.SalesTaxPayableAccountId,
            settings.PurchaseTaxReceivableAccountId,
            settings.PreventNegativeStock);
    }

    private async Task<string> GetBaseCurrencyCodeAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        return await db.Currencies
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsBase)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken)
            ?? "BASE";
    }

    private async Task<(Guid? CurrencyId, decimal ExchangeRate)>
        ResolveTradeCurrencyAsync(
            Guid companyId,
            Guid? requestedCurrencyId,
            decimal? requestedRate,
            DateOnly documentDate,
            CancellationToken cancellationToken)
    {
        if (requestedCurrencyId is not Guid currencyId)
        {
            return (null, 1m);
        }

        var currency = await db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == currencyId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Transaction currency does not exist or is inactive.");

        if (currency.IsBase)
        {
            if (requestedRate.HasValue &&
                requestedRate.Value != 1m)
            {
                throw new ArgumentException(
                    "Base-currency exchange rate must equal 1.");
            }

            return (null, 1m);
        }

        var rate = requestedRate;

        if (!rate.HasValue)
        {
            var rateRows = await db.CurrencyExchangeRates
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.CurrencyId == currency.Id &&
                    x.RateDate <= documentDate)
                .ToArrayAsync(cancellationToken);

            rate = rateRows
                .OrderByDescending(x => x.RateDate)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => (decimal?)x.AccountingRate)
                .FirstOrDefault();
        }

        if (!rate.HasValue || rate.Value <= 0)
        {
            throw new InvalidOperationException(
                $"No accounting exchange rate is available for {currency.Code} on or before {documentDate:yyyy-MM-dd}.");
        }

        return (currency.Id, rate.Value);
    }

    private static decimal ConvertToBase(
        decimal transactionAmount,
        decimal exchangeRate)
    {
        return Math.Round(
            transactionAmount * exchangeRate,
            4,
            MidpointRounding.AwayFromZero);
    }

    private static decimal ConvertFromBase(
        decimal baseAmount,
        decimal exchangeRate)
    {
        if (exchangeRate <= 0)
        {
            throw new ArgumentException(
                "Exchange rate must be positive.");
        }

        return Math.Round(
            baseAmount / exchangeRate,
            4,
            MidpointRounding.AwayFromZero);
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }
}
