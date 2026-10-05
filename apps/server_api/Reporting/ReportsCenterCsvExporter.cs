using System.Globalization;
using System.Text;
using ERPAccounting.Api.Contracts;

namespace ERPAccounting.Api.Reporting;

public static class ReportsCenterCsvExporter
{
    public static byte[] Export(ReportsCenterResponse report)
    {
        var csv = new StringBuilder();
        csv.AppendLine("section,code,name,metric,value");

        AppendSummary(csv, report.Summary);

        foreach (var row in report.Products)
        {
            Append(csv, "product", row.Sku, row.ProductName, "net_sales", row.NetSales);
            Append(csv, "product", row.Sku, row.ProductName, "gross_profit", row.GrossProfit);
            Append(csv, "product", row.Sku, row.ProductName, "net_sold_quantity", row.NetSoldQuantity);
            Append(csv, "product", row.Sku, row.ProductName, "net_purchased_quantity", row.NetPurchasedQuantity);
        }

        foreach (var row in report.Parties)
        {
            Append(csv, "party", row.Code, row.Name, "sales", row.Sales);
            Append(csv, "party", row.Code, row.Name, "purchases", row.Purchases);
            Append(csv, "party", row.Code, row.Name, "net_commercial_flow", row.NetCommercialFlow);
        }

        foreach (var row in report.Inventory)
        {
            var code = $"{row.WarehouseCode}/{row.Sku}";
            var name = $"{row.WarehouseName} / {row.ProductName}";
            Append(csv, "inventory", code, name, "opening_quantity", row.OpeningQuantity);
            Append(csv, "inventory", code, name, "in_quantity", row.InQuantity);
            Append(csv, "inventory", code, name, "out_quantity", row.OutQuantity);
            Append(csv, "inventory", code, name, "closing_quantity", row.ClosingQuantity);
            Append(csv, "inventory", code, name, "closing_value", row.ClosingValue);
        }

        foreach (var row in report.Treasury)
        {
            Append(csv, "treasury", row.Code, row.Name, "receipts", row.Receipts);
            Append(csv, "treasury", row.Code, row.Name, "payments", row.Payments);
            Append(csv, "treasury", row.Code, row.Name, "net_flow", row.NetFlow);
        }

        foreach (var row in report.CostCenters)
            AppendDimension(csv, "cost_center", row);

        foreach (var row in report.Projects)
            AppendDimension(csv, "project", row);

        // UTF-8 BOM keeps Persian names readable when the CSV is opened directly in Excel.
        var payload = Encoding.UTF8.GetBytes(csv.ToString());
        return [0xEF, 0xBB, 0xBF, .. payload];
    }

    private static void AppendSummary(StringBuilder csv, ReportKpiSummary summary)
    {
        Append(csv, "summary", "", "Reports Center", "net_sales", summary.NetSales);
        Append(csv, "summary", "", "Reports Center", "cost_of_goods_sold", summary.CostOfGoodsSold);
        Append(csv, "summary", "", "Reports Center", "gross_profit", summary.GrossProfit);
        Append(csv, "summary", "", "Reports Center", "net_purchases", summary.NetPurchases);
        Append(csv, "summary", "", "Reports Center", "treasury_net_flow", summary.TreasuryNetFlow);
        Append(csv, "summary", "", "Reports Center", "inventory_value", summary.InventoryValue);
    }

    private static void AppendDimension(
        StringBuilder csv,
        string section,
        DimensionPerformanceRow row)
    {
        Append(csv, section, row.Code, row.Name, "debit", row.Debit);
        Append(csv, section, row.Code, row.Name, "credit", row.Credit);
        Append(csv, section, row.Code, row.Name, "net", row.Net);
    }

    private static void Append(
        StringBuilder csv,
        string section,
        string code,
        string name,
        string metric,
        decimal value)
    {
        csv.Append(Escape(section)).Append(',')
            .Append(Escape(code)).Append(',')
            .Append(Escape(name)).Append(',')
            .Append(Escape(metric)).Append(',')
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .AppendLine();
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
