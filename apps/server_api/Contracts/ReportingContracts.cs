namespace ERPAccounting.Api.Contracts;

public sealed record ReportKpiSummary(
    decimal NetSales,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal NetPurchases,
    decimal TreasuryNetFlow,
    decimal InventoryValue);

public sealed record ProductPerformanceRow(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal SoldQuantity,
    decimal ReturnedQuantity,
    decimal NetSoldQuantity,
    decimal NetSales,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    decimal PurchasedQuantity,
    decimal PurchaseReturnedQuantity,
    decimal NetPurchasedQuantity,
    decimal NetPurchases);

public sealed record PartyPerformanceRow(
    Guid DetailAccountId,
    string Code,
    string Name,
    string Type,
    decimal Sales,
    decimal Purchases,
    decimal Receipts,
    decimal Payments,
    decimal NetCommercialFlow);

public sealed record InventoryPerformanceRow(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal OpeningQuantity,
    decimal InQuantity,
    decimal OutQuantity,
    decimal ClosingQuantity,
    decimal ClosingValue);

public sealed record TreasuryPerformanceRow(
    Guid TreasuryAccountId,
    string Code,
    string Name,
    string Type,
    decimal Receipts,
    decimal Payments,
    decimal TransfersIn,
    decimal TransfersOut,
    decimal NetFlow);

public sealed record DimensionPerformanceRow(
    Guid DimensionId,
    string Code,
    string Name,
    string DimensionType,
    decimal Debit,
    decimal Credit,
    decimal Net);

public sealed record ReportsCenterResponse(
    DateOnly? From,
    DateOnly? To,
    ReportKpiSummary Summary,
    IReadOnlyList<ProductPerformanceRow> Products,
    IReadOnlyList<PartyPerformanceRow> Parties,
    IReadOnlyList<InventoryPerformanceRow> Inventory,
    IReadOnlyList<TreasuryPerformanceRow> Treasury,
    IReadOnlyList<DimensionPerformanceRow> CostCenters,
    IReadOnlyList<DimensionPerformanceRow> Projects);
