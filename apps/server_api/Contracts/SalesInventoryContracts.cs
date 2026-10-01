using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Contracts;

public sealed record ProductView(
    Guid Id,
    string Sku,
    string Name,
    string? Barcode,
    string UnitName,
    ProductKind Kind,
    bool TrackInventory,
    decimal SalesPrice,
    decimal DefaultPurchasePrice,
    bool IsActive);

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Barcode,
    string UnitName,
    ProductKind Kind,
    bool TrackInventory,
    decimal SalesPrice,
    decimal DefaultPurchasePrice);

public sealed record UpdateProductRequest(
    string Sku,
    string Name,
    string? Barcode,
    string UnitName,
    ProductKind Kind,
    bool TrackInventory,
    decimal SalesPrice,
    decimal DefaultPurchasePrice,
    bool IsActive);

public sealed record WarehouseView(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public sealed record CreateWarehouseRequest(
    string Code,
    string Name);

public sealed record UpdateWarehouseRequest(
    string Code,
    string Name,
    bool IsActive);

public sealed record SalesInventorySettingsView(
    Guid ReceivablesAccountId,
    Guid CashAccountId,
    Guid SalesRevenueAccountId,
    Guid InventoryAccountId,
    Guid CostOfGoodsSoldAccountId,
    Guid SalesTaxPayableAccountId,
    bool PreventNegativeStock);

public sealed record UpdateSalesInventorySettingsRequest(
    Guid ReceivablesAccountId,
    Guid CashAccountId,
    Guid SalesRevenueAccountId,
    Guid InventoryAccountId,
    Guid CostOfGoodsSoldAccountId,
    Guid SalesTaxPayableAccountId,
    bool PreventNegativeStock);

public sealed record StockBalanceView(
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid WarehouseId,
    string WarehouseName,
    decimal Quantity,
    decimal AverageCost,
    decimal InventoryValue);

public sealed record CreateStockAdjustmentRequest(
    Guid WarehouseId,
    Guid ProductId,
    DateOnly DocumentDate,
    decimal QuantityDelta,
    decimal? UnitCost,
    string Reason);

public sealed record SalesInvoiceLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount);

public sealed record CreateSalesInvoiceRequest(
    Guid FiscalYearId,
    DateOnly DocumentDate,
    Guid WarehouseId,
    Guid? CustomerDetailAccountId,
    SalesPaymentType PaymentType,
    string? Description,
    IReadOnlyList<SalesInvoiceLineRequest> Lines);

public sealed record SalesInvoiceLineView(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetAmount,
    decimal UnitCost,
    decimal CostAmount);

public sealed record SalesInvoiceView(
    Guid Id,
    Guid FiscalYearId,
    string Number,
    DateOnly DocumentDate,
    Guid? CustomerDetailAccountId,
    string? CustomerName,
    Guid WarehouseId,
    string WarehouseName,
    SalesPaymentType PaymentType,
    SalesInvoiceStatus Status,
    string? Description,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal CostTotal,
    Guid? AccountingJournalEntryId,
    string? AccountingJournalNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt,
    IReadOnlyList<SalesInvoiceLineView> Lines);

public sealed record PostSalesInvoiceResponse(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid AccountingJournalEntryId,
    string AccountingJournalNumber,
    decimal GrandTotal,
    decimal CostTotal);
