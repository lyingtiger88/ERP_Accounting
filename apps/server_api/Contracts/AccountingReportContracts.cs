namespace ERPAccounting.Api.Contracts;

public sealed record TrialBalanceRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal OpeningBalance,
    decimal DebitTurnover,
    decimal CreditTurnover,
    decimal Balance);

public sealed record GeneralLedgerRow(
    Guid JournalId,
    string JournalNumber,
    DateOnly DocumentDate,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

public sealed record GeneralLedgerReportResponse(
    Guid? AccountId,
    decimal OpeningBalance,
    decimal DebitTurnover,
    decimal CreditTurnover,
    decimal ClosingBalance,
    IReadOnlyList<GeneralLedgerRow> Rows);


public sealed record ProfitLossRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string Section,
    decimal Amount);

public sealed record ProfitLossReportResponse(
    DateOnly? From,
    DateOnly? To,
    decimal RevenueTotal,
    decimal ExpenseTotal,
    decimal NetProfit,
    IReadOnlyList<ProfitLossRow> Rows);

public sealed record BalanceSheetRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string Section,
    decimal Amount);

public sealed record BalanceSheetReportResponse(
    DateOnly AsOf,
    decimal AssetTotal,
    decimal LiabilityTotal,
    decimal EquityTotal,
    decimal AccumulatedResult,
    decimal RightSideTotal,
    decimal Difference,
    IReadOnlyList<BalanceSheetRow> Rows);


public sealed record DetailLedgerRow(
    Guid JournalId,
    string JournalNumber,
    DateOnly DocumentDate,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    Guid DetailAccountId,
    string DetailCode,
    string DetailName,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

public sealed record DetailLedgerReportResponse(
    Guid DetailAccountId,
    decimal OpeningBalance,
    decimal DebitTurnover,
    decimal CreditTurnover,
    decimal ClosingBalance,
    IReadOnlyList<DetailLedgerRow> Rows);
