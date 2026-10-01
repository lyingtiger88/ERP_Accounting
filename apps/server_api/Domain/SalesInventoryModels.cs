namespace ERPAccounting.Api.Domain;

public enum ProductKind
{
    Inventory,
    Service
}

public enum InventoryTrackingMode
{
    None,
    Lot,
    Serial
}

public enum SalesInvoiceStatus
{
    Draft,
    Posted,
    Reversed
}

public enum SalesPaymentType
{
    Credit,
    Cash
}

public enum PurchaseReceiptStatus
{
    Draft,
    Posted,
    Reversed
}

public enum PurchasePaymentType
{
    Credit,
    Cash
}

public enum WarehouseTransferStatus
{
    Draft,
    Posted,
    Reversed
}

public enum SalesReturnStatus
{
    Posted,
    Reversed
}

public enum StockMovementType
{
    Opening,
    PurchaseReceipt,
    SaleIssue,
    SaleReturn,
    TransferOut,
    TransferIn,
    AdjustmentIn,
    AdjustmentOut
}

public sealed class StoreProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public string? Barcode { get; set; }
    public string UnitName { get; set; } = "عدد";
    public ProductKind Kind { get; set; } = ProductKind.Inventory;
    public bool TrackInventory { get; set; } = true;
    public InventoryTrackingMode TrackingMode { get; set; } = InventoryTrackingMode.None;
    public decimal MinimumStock { get; set; }
    public decimal SalesPrice { get; set; }
    public decimal DefaultPurchasePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Warehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SalesInventorySettings
{
    public required Guid CompanyId { get; set; }
    public required Guid ReceivablesAccountId { get; set; }
    public required Guid PayablesAccountId { get; set; }
    public required Guid CashAccountId { get; set; }
    public required Guid SalesRevenueAccountId { get; set; }
    public required Guid InventoryAccountId { get; set; }
    public required Guid CostOfGoodsSoldAccountId { get; set; }
    public required Guid SalesTaxPayableAccountId { get; set; }
    public required Guid PurchaseTaxReceivableAccountId { get; set; }
    public bool PreventNegativeStock { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SalesInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid FiscalYearId { get; set; }
    public required string Number { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public Guid? CustomerDetailAccountId { get; set; }
    public required Guid WarehouseId { get; set; }
    public SalesPaymentType PaymentType { get; set; } = SalesPaymentType.Credit;
    public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Draft;
    public string? Description { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal CostTotal { get; set; }
    public Guid? AccountingJournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = [];
}

public sealed class SalesInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid SalesInvoiceId { get; set; }
    public required Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal UnitCost { get; set; }
    public decimal CostAmount { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class PurchaseReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid FiscalYearId { get; set; }
    public required string Number { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public Guid? SupplierDetailAccountId { get; set; }
    public required Guid WarehouseId { get; set; }
    public PurchasePaymentType PaymentType { get; set; } = PurchasePaymentType.Credit;
    public PurchaseReceiptStatus Status { get; set; } = PurchaseReceiptStatus.Draft;
    public string? Description { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public Guid? AccountingJournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
    public List<PurchaseReceiptLine> Lines { get; set; } = [];
}

public sealed class PurchaseReceiptLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid PurchaseReceiptId { get; set; }
    public required Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class WarehouseTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Number { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public required Guid FromWarehouseId { get; set; }
    public required Guid ToWarehouseId { get; set; }
    public WarehouseTransferStatus Status { get; set; } = WarehouseTransferStatus.Draft;
    public string? Description { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
    public List<WarehouseTransferLine> Lines { get; set; } = [];
}

public sealed class WarehouseTransferLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid WarehouseTransferId { get; set; }
    public required Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}

public sealed class SalesReturn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid FiscalYearId { get; set; }
    public required Guid SalesInvoiceId { get; set; }
    public required string Number { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public required Guid WarehouseId { get; set; }
    public SalesReturnStatus Status { get; set; } = SalesReturnStatus.Posted;
    public string? Reason { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal CostTotal { get; set; }
    public Guid? AccountingJournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SalesReturnLine> Lines { get; set; } = [];
}

public sealed class SalesReturnLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid SalesReturnId { get; set; }
    public required Guid SalesInvoiceLineId { get; set; }
    public required Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal UnitCost { get; set; }
    public decimal CostAmount { get; set; }
}

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid WarehouseId { get; set; }
    public required Guid ProductId { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
