namespace ERPAccounting.Api.Domain;

public enum ProductKind
{
    Inventory,
    Service
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

public enum StockMovementType
{
    Opening,
    PurchaseReceipt,
    SaleIssue,
    SaleReturn,
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
    public required Guid CashAccountId { get; set; }
    public required Guid SalesRevenueAccountId { get; set; }
    public required Guid InventoryAccountId { get; set; }
    public required Guid CostOfGoodsSoldAccountId { get; set; }
    public required Guid SalesTaxPayableAccountId { get; set; }
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
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
