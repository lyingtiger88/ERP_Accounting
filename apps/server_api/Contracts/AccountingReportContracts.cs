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
