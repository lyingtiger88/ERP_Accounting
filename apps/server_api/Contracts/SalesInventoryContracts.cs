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
    bool IsActive,
    InventoryTrackingMode TrackingMode,
    decimal MinimumStock);

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Barcode,
    string UnitName,
    ProductKind Kind,
    bool TrackInventory,
    decimal SalesPrice,
    decimal DefaultPurchasePrice,
    InventoryTrackingMode TrackingMode = InventoryTrackingMode.None,
    decimal MinimumStock = 0);

public sealed record UpdateProductRequest(
    string Sku,
    string Name,
    string? Barcode,
    string UnitName,
    ProductKind Kind,
    bool TrackInventory,
    decimal SalesPrice,
    decimal DefaultPurchasePrice,
    bool IsActive,
    InventoryTrackingMode TrackingMode = InventoryTrackingMode.None,
    decimal MinimumStock = 0);

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
    Guid PayablesAccountId,
    Guid CashAccountId,
    Guid SalesRevenueAccountId,
    Guid InventoryAccountId,
    Guid CostOfGoodsSoldAccountId,
    Guid SalesTaxPayableAccountId,
    Guid PurchaseTaxReceivableAccountId,
    bool PreventNegativeStock);

public sealed record UpdateSalesInventorySettingsRequest(
    Guid ReceivablesAccountId,
    Guid CashAccountId,
    Guid SalesRevenueAccountId,
    Guid InventoryAccountId,
    Guid CostOfGoodsSoldAccountId,
    Guid SalesTaxPayableAccountId,
    bool PreventNegativeStock,
    Guid? PayablesAccountId = null,
    Guid? PurchaseTaxReceivableAccountId = null);

public sealed record StockBalanceView(
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid WarehouseId,
    string WarehouseName,
    decimal Quantity,
    decimal AverageCost,
    decimal InventoryValue,
    decimal MinimumStock,
    bool IsLowStock);

public sealed record StockTraceBalanceView(
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid WarehouseId,
    string WarehouseName,
    string? LotNumber,
    string? SerialNumber,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal AverageCost);

public sealed record LowStockAlertView(
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid WarehouseId,
    string WarehouseName,
    decimal Quantity,
    decimal MinimumStock,
    decimal Shortage);

public sealed record CreateStockAdjustmentRequest(
    Guid WarehouseId,
    Guid ProductId,
    DateOnly DocumentDate,
    decimal QuantityDelta,
    decimal? UnitCost,
    string Reason,
    string? LotNumber = null,
    string? SerialNumber = null,
    DateOnly? ExpiryDate = null);

public sealed record WarehouseTransferLineRequest(
    Guid ProductId,
    decimal Quantity,
    string? LotNumber = null,
    string? SerialNumber = null,
    DateOnly? ExpiryDate = null);

public sealed record CreateWarehouseTransferRequest(
    DateOnly DocumentDate,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    string? Description,
    IReadOnlyList<WarehouseTransferLineRequest> Lines);

public sealed record WarehouseTransferLineView(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    string? LotNumber,
    string? SerialNumber,
    DateOnly? ExpiryDate);

public sealed record WarehouseTransferView(
    Guid Id,
    string Number,
    DateOnly DocumentDate,
    Guid FromWarehouseId,
    string FromWarehouseName,
    Guid ToWarehouseId,
    string ToWarehouseName,
    WarehouseTransferStatus Status,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt,
    IReadOnlyList<WarehouseTransferLineView> Lines);

public sealed record SalesInvoiceLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    string? LotNumber = null,
    string? SerialNumber = null,
    DateOnly? ExpiryDate = null);

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
    decimal CostAmount,
    string? LotNumber,
    string? SerialNumber,
    DateOnly? ExpiryDate);

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

public sealed record PurchaseReceiptLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal? UnitCost,
    decimal DiscountAmount,
    decimal TaxAmount,
    string? LotNumber = null,
    string? SerialNumber = null,
    DateOnly? ExpiryDate = null);

public sealed record CreatePurchaseReceiptRequest(
    Guid FiscalYearId,
    DateOnly DocumentDate,
    Guid WarehouseId,
    Guid? SupplierDetailAccountId,
    PurchasePaymentType PaymentType,
    string? Description,
    IReadOnlyList<PurchaseReceiptLineRequest> Lines);

public sealed record PurchaseReceiptLineView(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    string UnitName,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal NetAmount,
    string? LotNumber,
    string? SerialNumber,
    DateOnly? ExpiryDate);

public sealed record PurchaseReceiptView(
    Guid Id,
    Guid FiscalYearId,
    string Number,
    DateOnly DocumentDate,
    Guid? SupplierDetailAccountId,
    string? SupplierName,
    Guid WarehouseId,
    string WarehouseName,
    PurchasePaymentType PaymentType,
    PurchaseReceiptStatus Status,
    string? Description,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    Guid? AccountingJournalEntryId,
    string? AccountingJournalNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt,
    IReadOnlyList<PurchaseReceiptLineView> Lines);

public sealed record PostPurchaseReceiptResponse(
    Guid ReceiptId,
    string ReceiptNumber,
    Guid AccountingJournalEntryId,
    string AccountingJournalNumber,
    decimal GrandTotal);

public sealed record SalesReturnLineRequest(
    Guid SalesInvoiceLineId,
    decimal Quantity);

public sealed record CreateSalesReturnRequest(
    DateOnly DocumentDate,
    string Reason,
    IReadOnlyList<SalesReturnLineRequest> Lines);

public sealed record SalesReturnLineView(
    Guid Id,
    Guid SalesInvoiceLineId,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal NetAmount,
    decimal TaxAmount,
    decimal UnitCost,
    decimal CostAmount);

public sealed record SalesReturnView(
    Guid Id,
    Guid SalesInvoiceId,
    string Number,
    DateOnly DocumentDate,
    Guid WarehouseId,
    string WarehouseName,
    SalesReturnStatus Status,
    string? Reason,
    decimal GrandTotal,
    decimal TaxTotal,
    decimal CostTotal,
    Guid? AccountingJournalEntryId,
    string? AccountingJournalNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SalesReturnLineView> Lines);
