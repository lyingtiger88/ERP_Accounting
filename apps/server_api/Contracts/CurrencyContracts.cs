using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Contracts;

public sealed record CreateCurrencyRequest(
    string Code,
    string Name,
    string? Symbol,
    int DecimalPlaces = 2);

public sealed record SetBaseCurrencyRequest(
    Guid CurrencyId);

public sealed record SetCurrencyStateRequest(
    bool IsActive);

public sealed record CreateCurrencyRateRequest(
    Guid CurrencyId,
    DateOnly RateDate,
    decimal BuyRate,
    decimal SellRate,
    decimal AccountingRate,
    string? Source);

public sealed record CurrencyView(
    Guid Id,
    string Code,
    string Name,
    string? Symbol,
    int DecimalPlaces,
    bool IsBase,
    bool IsActive);

public sealed record CurrencyRateView(
    Guid Id,
    Guid CurrencyId,
    string CurrencyCode,
    DateOnly RateDate,
    decimal BuyRate,
    decimal SellRate,
    decimal AccountingRate,
    string Source,
    DateTimeOffset CreatedAt);

public sealed record CreateForeignCurrencyJournalLineRequest(
    Guid AccountId,
    string? Description,
    decimal ForeignDebit,
    decimal ForeignCredit,
    Guid? DetailAccountId = null,
    Guid? CostCenterId = null,
    Guid? ProjectId = null);

public sealed record CreateForeignCurrencyJournalRequest(
    Guid CurrencyId,
    DateOnly DocumentDate,
    string? Description,
    IReadOnlyList<CreateForeignCurrencyJournalLineRequest> Lines,
    Guid? FiscalYearId = null,
    decimal? ExchangeRate = null);

public sealed record ForeignCurrencyJournalResponse(
    Guid JournalEntryId,
    string Number,
    Guid CurrencyId,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal ForeignDebitTotal,
    decimal ForeignCreditTotal,
    decimal BaseDebitTotal,
    decimal BaseCreditTotal);

public sealed record CurrencyPositionRow(
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencyName,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal ForeignBalance,
    decimal HistoricalBaseBalance,
    decimal CurrentRate,
    decimal RevaluedBaseBalance,
    decimal UnrealizedDifference);

public sealed record CurrencyPositionReport(
    DateOnly AsOf,
    string BaseCurrencyCode,
    IReadOnlyList<CurrencyPositionRow> Rows);


public sealed record CurrencyRevaluationRequest(
    DateOnly AsOf,
    Guid GainAccountId,
    Guid LossAccountId,
    Guid? FiscalYearId = null,
    string? Description = null);

public sealed record CurrencyRevaluationResponse(
    DateOnly AsOf,
    Guid? JournalEntryId,
    string? JournalNumber,
    int PositionCount,
    decimal GainTotal,
    decimal LossTotal,
    bool NoAdjustmentRequired);
