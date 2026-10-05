using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Reporting;

public sealed class ReportingService(AppDbContext db)
{
    public async Task<ReportsCenterResponse> GetReportsCenterAsync(
        Guid companyId,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? warehouseId = null,
        Guid? productId = null,
        Guid? detailAccountId = null,
        CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            throw new ArgumentException(
                "Report start date cannot be after end date.");
        }

        bool InRange(DateOnly date) =>
            (!from.HasValue || date >= from.Value) &&
            (!to.HasValue || date <= to.Value);

        var products = await db.StoreProducts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (!productId.HasValue || x.Id == productId.Value))
            .ToArrayAsync(cancellationToken);
        var productMap = products.ToDictionary(x => x.Id);

        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (!warehouseId.HasValue || x.Id == warehouseId.Value))
            .ToArrayAsync(cancellationToken);
        var warehouseMap = warehouses.ToDictionary(x => x.Id);

        var details = await db.DetailAccounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (!detailAccountId.HasValue ||
                 x.Id == detailAccountId.Value))
            .ToArrayAsync(cancellationToken);
        var detailMap = details.ToDictionary(x => x.Id);

        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status != SalesInvoiceStatus.Draft)
            .ToArrayAsync(cancellationToken);

        invoices = invoices
            .Where(x =>
                InRange(x.DocumentDate) &&
                (!warehouseId.HasValue ||
                 x.WarehouseId == warehouseId.Value) &&
                (!detailAccountId.HasValue ||
                 x.CustomerDetailAccountId == detailAccountId.Value))
            .ToArray();

        var salesReturns = await db.SalesReturns
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == SalesReturnStatus.Posted)
            .ToArrayAsync(cancellationToken);

        salesReturns = salesReturns
            .Where(x =>
                InRange(x.DocumentDate) &&
                (!warehouseId.HasValue ||
                 x.WarehouseId == warehouseId.Value))
            .ToArray();

        var receipts = await db.PurchaseReceipts
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status != PurchaseReceiptStatus.Draft)
            .ToArrayAsync(cancellationToken);

        receipts = receipts
            .Where(x =>
                InRange(x.DocumentDate) &&
                (!warehouseId.HasValue ||
                 x.WarehouseId == warehouseId.Value) &&
                (!detailAccountId.HasValue ||
                 x.SupplierDetailAccountId == detailAccountId.Value))
            .ToArray();

        var purchaseReturns = await db.PurchaseReturns
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == PurchaseReturnStatus.Posted)
            .ToArrayAsync(cancellationToken);

        purchaseReturns = purchaseReturns
            .Where(x =>
                InRange(x.DocumentDate) &&
                (!warehouseId.HasValue ||
                 x.WarehouseId == warehouseId.Value))
            .ToArray();

        var invoiceById = (await db.SalesInvoices
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        var receiptById = (await db.PurchaseReceipts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        var salesNet = invoices.Sum(x =>
            (x.Subtotal - x.DiscountTotal) * SafeRate(x.ExchangeRate));
        var salesReturnNet = salesReturns.Sum(x =>
            (x.GrandTotal - x.TaxTotal) *
            (invoiceById.TryGetValue(x.SalesInvoiceId, out var invoice)
                ? SafeRate(invoice.ExchangeRate)
                : 1m));
        var cogs = invoices.Sum(x => x.CostTotal) -
            salesReturns.Sum(x => x.CostTotal);
        var netSales = salesNet - salesReturnNet;

        var purchasesNet = receipts.Sum(x =>
            (x.Subtotal - x.DiscountTotal) * SafeRate(x.ExchangeRate));
        var purchaseReturnNet = purchaseReturns.Sum(x =>
            (x.GrandTotal - x.TaxTotal) *
            (receiptById.TryGetValue(x.PurchaseReceiptId, out var receipt)
                ? SafeRate(receipt.ExchangeRate)
                : 1m));
        var netPurchases = purchasesNet - purchaseReturnNet;

        var productRows = BuildProductRows(
            products,
            invoices,
            salesReturns,
            receipts,
            purchaseReturns,
            invoiceById,
            receiptById);

        var stockMovements = await db.StockMovements
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (!warehouseId.HasValue ||
                 x.WarehouseId == warehouseId.Value) &&
                (!productId.HasValue ||
                 x.ProductId == productId.Value))
            .ToArrayAsync(cancellationToken);

        if (to.HasValue)
        {
            stockMovements = stockMovements
                .Where(x => x.DocumentDate <= to.Value)
                .ToArray();
        }

        var inventoryRows = stockMovements
            .GroupBy(x => new { x.WarehouseId, x.ProductId })
            .Where(group =>
                warehouseMap.ContainsKey(group.Key.WarehouseId) &&
                productMap.ContainsKey(group.Key.ProductId))
            .Select(group =>
            {
                var opening = from.HasValue
                    ? group
                        .Where(x => x.DocumentDate < from.Value)
                        .Sum(x => x.Quantity)
                    : 0m;
                var period = group
                    .Where(x =>
                        (!from.HasValue ||
                         x.DocumentDate >= from.Value) &&
                        (!to.HasValue ||
                         x.DocumentDate <= to.Value))
                    .ToArray();
                var incoming = period
                    .Where(x => x.Quantity > 0)
                    .Sum(x => x.Quantity);
                var outgoing = -period
                    .Where(x => x.Quantity < 0)
                    .Sum(x => x.Quantity);
                var closing = group.Sum(x => x.Quantity);
                var closingValue = group.Sum(
                    x => x.Quantity * x.UnitCost);
                var wh = warehouseMap[group.Key.WarehouseId];
                var product = productMap[group.Key.ProductId];

                return new InventoryPerformanceRow(
                    wh.Id,
                    wh.Code,
                    wh.Name,
                    product.Id,
                    product.Sku,
                    product.Name,
                    opening,
                    incoming,
                    outgoing,
                    closing,
                    closingValue);
            })
            .OrderBy(x => x.WarehouseCode)
            .ThenBy(x => x.Sku)
            .ToArray();

        var treasuryAccounts = await db.TreasuryAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);
        var treasuryMap = treasuryAccounts.ToDictionary(x => x.Id);

        var treasuryTransactions = await db.TreasuryTransactions
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == TreasuryTransactionStatus.Posted)
            .ToArrayAsync(cancellationToken);

        treasuryTransactions = treasuryTransactions
            .Where(x =>
                InRange(x.DocumentDate) &&
                (!detailAccountId.HasValue ||
                 x.DetailAccountId == detailAccountId.Value))
            .ToArray();

        var treasuryRows = treasuryAccounts
            .Select(account =>
            {
                var received = treasuryTransactions
                    .Where(x =>
                        x.Type == TreasuryTransactionType.Receipt &&
                        x.ToTreasuryAccountId == account.Id)
                    .Sum(BaseAmount);
                var paid = treasuryTransactions
                    .Where(x =>
                        x.Type == TreasuryTransactionType.Payment &&
                        x.FromTreasuryAccountId == account.Id)
                    .Sum(BaseAmount);
                var transferIn = treasuryTransactions
                    .Where(x =>
                        x.Type == TreasuryTransactionType.Transfer &&
                        x.ToTreasuryAccountId == account.Id)
                    .Sum(BaseAmount);
                var transferOut = treasuryTransactions
                    .Where(x =>
                        x.Type == TreasuryTransactionType.Transfer &&
                        x.FromTreasuryAccountId == account.Id)
                    .Sum(BaseAmount);

                return new TreasuryPerformanceRow(
                    account.Id,
                    account.Code,
                    account.Name,
                    account.Type.ToString(),
                    received,
                    paid,
                    transferIn,
                    transferOut,
                    received - paid + transferIn - transferOut);
            })
            .OrderBy(x => x.Code)
            .ToArray();

        var partyRows = BuildPartyRows(
            details,
            invoices,
            salesReturns,
            receipts,
            purchaseReturns,
            treasuryTransactions,
            invoiceById,
            receiptById);

        var (costCenterRows, projectRows) =
            await BuildDimensionRowsAsync(
                companyId,
                from,
                to,
                cancellationToken);

        var inventoryValue = inventoryRows.Sum(x => x.ClosingValue);
        var receiptTotal = treasuryTransactions
            .Where(x => x.Type == TreasuryTransactionType.Receipt)
            .Sum(BaseAmount);
        var paymentTotal = treasuryTransactions
            .Where(x => x.Type == TreasuryTransactionType.Payment)
            .Sum(BaseAmount);

        return new ReportsCenterResponse(
            from,
            to,
            new ReportKpiSummary(
                netSales,
                cogs,
                netSales - cogs,
                netPurchases,
                receiptTotal - paymentTotal,
                inventoryValue),
            productRows,
            partyRows,
            inventoryRows,
            treasuryRows,
            costCenterRows,
            projectRows);
    }

    private static ProductPerformanceRow[] BuildProductRows(
        IReadOnlyList<StoreProduct> products,
        IReadOnlyList<SalesInvoice> invoices,
        IReadOnlyList<SalesReturn> salesReturns,
        IReadOnlyList<PurchaseReceipt> receipts,
        IReadOnlyList<PurchaseReturn> purchaseReturns,
        IReadOnlyDictionary<Guid, SalesInvoice> invoiceById,
        IReadOnlyDictionary<Guid, PurchaseReceipt> receiptById)
    {
        return products.Select(product =>
        {
            var saleLines = invoices
                .SelectMany(invoice => invoice.Lines.Select(line =>
                    new { Invoice = invoice, Line = line }))
                .Where(x => x.Line.ProductId == product.Id)
                .ToArray();

            var returnLines = salesReturns
                .SelectMany(ret => ret.Lines.Select(line =>
                    new { Return = ret, Line = line }))
                .Where(x => x.Line.ProductId == product.Id)
                .ToArray();

            var purchaseLines = receipts
                .SelectMany(receipt => receipt.Lines.Select(line =>
                    new { Receipt = receipt, Line = line }))
                .Where(x => x.Line.ProductId == product.Id)
                .ToArray();

            var purchaseReturnLines = purchaseReturns
                .SelectMany(ret => ret.Lines.Select(line =>
                    new { Return = ret, Line = line }))
                .Where(x => x.Line.ProductId == product.Id)
                .ToArray();

            var soldQty = saleLines.Sum(x => x.Line.Quantity);
            var returnedQty = returnLines.Sum(x => x.Line.Quantity);
            var sales = saleLines.Sum(x =>
                x.Line.NetAmount * SafeRate(x.Invoice.ExchangeRate));
            var salesReturnsAmount = returnLines.Sum(x =>
                x.Line.NetAmount *
                (invoiceById.TryGetValue(
                    x.Return.SalesInvoiceId,
                    out var invoice)
                    ? SafeRate(invoice.ExchangeRate)
                    : 1m));
            var productCogs = saleLines.Sum(x => x.Line.CostAmount) -
                returnLines.Sum(x => x.Line.CostAmount);
            var netProductSales = sales - salesReturnsAmount;
            var grossProfit = netProductSales - productCogs;
            var margin = netProductSales == 0
                ? 0
                : grossProfit / netProductSales * 100m;

            var purchasedQty = purchaseLines.Sum(x => x.Line.Quantity);
            var purchaseReturnedQty =
                purchaseReturnLines.Sum(x => x.Line.Quantity);
            var purchases = purchaseLines.Sum(x =>
                x.Line.NetAmount * SafeRate(x.Receipt.ExchangeRate));
            var purchaseReturnsAmount = purchaseReturnLines.Sum(x =>
                x.Line.NetAmount *
                (receiptById.TryGetValue(
                    x.Return.PurchaseReceiptId,
                    out var receipt)
                    ? SafeRate(receipt.ExchangeRate)
                    : 1m));

            return new ProductPerformanceRow(
                product.Id,
                product.Sku,
                product.Name,
                soldQty,
                returnedQty,
                soldQty - returnedQty,
                netProductSales,
                productCogs,
                grossProfit,
                margin,
                purchasedQty,
                purchaseReturnedQty,
                purchasedQty - purchaseReturnedQty,
                purchases - purchaseReturnsAmount);
        })
        .Where(x =>
            x.NetSoldQuantity != 0 ||
            x.NetPurchasedQuantity != 0 ||
            x.NetSales != 0 ||
            x.NetPurchases != 0)
        .OrderByDescending(x => x.NetSales)
        .ThenBy(x => x.Sku)
        .ToArray();
    }

    private static PartyPerformanceRow[] BuildPartyRows(
        IReadOnlyList<DetailAccount> details,
        IReadOnlyList<SalesInvoice> invoices,
        IReadOnlyList<SalesReturn> salesReturns,
        IReadOnlyList<PurchaseReceipt> receipts,
        IReadOnlyList<PurchaseReturn> purchaseReturns,
        IReadOnlyList<TreasuryTransaction> treasuryTransactions,
        IReadOnlyDictionary<Guid, SalesInvoice> invoiceById,
        IReadOnlyDictionary<Guid, PurchaseReceipt> receiptById)
    {
        return details.Select(detail =>
        {
            var sales = invoices
                .Where(x => x.CustomerDetailAccountId == detail.Id)
                .Sum(x => x.GrandTotal * SafeRate(x.ExchangeRate));

            var salesReturnAmount = salesReturns
                .Where(x =>
                    invoiceById.TryGetValue(
                        x.SalesInvoiceId,
                        out var invoice) &&
                    invoice.CustomerDetailAccountId == detail.Id)
                .Sum(x =>
                    x.GrandTotal *
                    SafeRate(invoiceById[x.SalesInvoiceId].ExchangeRate));

            var purchases = receipts
                .Where(x => x.SupplierDetailAccountId == detail.Id)
                .Sum(x => x.GrandTotal * SafeRate(x.ExchangeRate));

            var purchaseReturnAmount = purchaseReturns
                .Where(x =>
                    receiptById.TryGetValue(
                        x.PurchaseReceiptId,
                        out var receipt) &&
                    receipt.SupplierDetailAccountId == detail.Id)
                .Sum(x =>
                    x.GrandTotal *
                    SafeRate(
                        receiptById[x.PurchaseReceiptId].ExchangeRate));

            var received = treasuryTransactions
                .Where(x =>
                    x.DetailAccountId == detail.Id &&
                    x.Type == TreasuryTransactionType.Receipt)
                .Sum(BaseAmount);

            var paid = treasuryTransactions
                .Where(x =>
                    x.DetailAccountId == detail.Id &&
                    x.Type == TreasuryTransactionType.Payment)
                .Sum(BaseAmount);

            var netSales = sales - salesReturnAmount;
            var netPurchases = purchases - purchaseReturnAmount;
            var exposure = detail.Type switch
            {
                DetailAccountType.Customer => netSales - received,
                DetailAccountType.Supplier => netPurchases - paid,
                _ => netSales - received - netPurchases + paid
            };

            return new PartyPerformanceRow(
                detail.Id,
                detail.Code,
                detail.Name,
                detail.Type.ToString(),
                netSales,
                netPurchases,
                received,
                paid,
                exposure);
        })
        .Where(x =>
            x.Sales != 0 ||
            x.Purchases != 0 ||
            x.Receipts != 0 ||
            x.Payments != 0)
        .OrderByDescending(x => Math.Abs(x.NetCommercialFlow))
        .ThenBy(x => x.Code)
        .ToArray();
    }

    private async Task<(
        DimensionPerformanceRow[] CostCenters,
        DimensionPerformanceRow[] Projects)> BuildDimensionRowsAsync(
        Guid companyId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var journals = await db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status != JournalStatus.Draft)
            .ToArrayAsync(cancellationToken);

        journals = journals
            .Where(x =>
                (!from.HasValue || x.DocumentDate >= from.Value) &&
                (!to.HasValue || x.DocumentDate <= to.Value))
            .ToArray();

        var lines = journals
            .SelectMany(x => x.Lines)
            .ToArray();
        var lineMap = lines.ToDictionary(x => x.Id);
        var lineIds = lineMap.Keys.ToArray();

        var dimensions = lineIds.Length == 0
            ? []
            : await db.JournalLineDimensions
                .AsNoTracking()
                .Where(x => lineIds.Contains(x.JournalLineId))
                .ToArrayAsync(cancellationToken);

        var costCenters = await db.CostCenters
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var projects = await db.AccountingProjects
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var costRows = dimensions
            .Where(x =>
                x.CostCenterId.HasValue &&
                costCenters.ContainsKey(x.CostCenterId.Value))
            .GroupBy(x => x.CostCenterId!.Value)
            .Select(group =>
            {
                var center = costCenters[group.Key];
                var debit = group.Sum(x =>
                    lineMap[x.JournalLineId].Debit);
                var credit = group.Sum(x =>
                    lineMap[x.JournalLineId].Credit);

                return new DimensionPerformanceRow(
                    center.Id,
                    center.Code,
                    center.Name,
                    "CostCenter",
                    debit,
                    credit,
                    debit - credit);
            })
            .OrderBy(x => x.Code)
            .ToArray();

        var projectRows = dimensions
            .Where(x =>
                x.ProjectId.HasValue &&
                projects.ContainsKey(x.ProjectId.Value))
            .GroupBy(x => x.ProjectId!.Value)
            .Select(group =>
            {
                var project = projects[group.Key];
                var debit = group.Sum(x =>
                    lineMap[x.JournalLineId].Debit);
                var credit = group.Sum(x =>
                    lineMap[x.JournalLineId].Credit);

                return new DimensionPerformanceRow(
                    project.Id,
                    project.Code,
                    project.Name,
                    "Project",
                    debit,
                    credit,
                    debit - credit);
            })
            .OrderBy(x => x.Code)
            .ToArray();

        return (costRows, projectRows);
    }

    private static decimal SafeRate(decimal value) =>
        value > 0 ? value : 1m;

    private static decimal BaseAmount(TreasuryTransaction value) =>
        value.Amount * SafeRate(value.ExchangeRate);
}
