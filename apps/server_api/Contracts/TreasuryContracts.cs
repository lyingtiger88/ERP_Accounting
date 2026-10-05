using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Contracts;

public sealed record CreateTreasuryAccountRequest(
    string Code,
    string Name,
    TreasuryAccountType Type,
    Guid LedgerAccountId,
    Guid? CurrencyId = null);

public sealed record UpdateTreasuryAccountRequest(
    string Code,
    string Name,
    TreasuryAccountType Type,
    Guid LedgerAccountId,
    Guid? CurrencyId,
    bool IsActive);

public sealed record TreasuryAccountView(
    Guid Id,
    string Code,
    string Name,
    TreasuryAccountType Type,
    Guid LedgerAccountId,
    string LedgerAccountCode,
    string LedgerAccountName,
    Guid? CurrencyId,
    string CurrencyCode,
    bool IsActive);

public sealed record CreateTreasuryTransactionRequest(
    Guid FiscalYearId,
    DateOnly DocumentDate,
    TreasuryTransactionType Type,
    decimal Amount,
    string? Description,
    Guid? FromTreasuryAccountId = null,
    Guid? ToTreasuryAccountId = null,
    Guid? CounterAccountId = null,
    Guid? DetailAccountId = null,
    Guid? CurrencyId = null,
    decimal? ExchangeRate = null);

public sealed record TreasuryTransactionView(
    Guid Id,
    Guid FiscalYearId,
    string Number,
    DateOnly DocumentDate,
    TreasuryTransactionType Type,
    TreasuryTransactionStatus Status,
    Guid? FromTreasuryAccountId,
    string? FromTreasuryAccountName,
    Guid? ToTreasuryAccountId,
    string? ToTreasuryAccountName,
    Guid? CounterAccountId,
    string? CounterAccountCode,
    string? CounterAccountName,
    Guid? DetailAccountId,
    string? DetailAccountName,
    Guid? CurrencyId,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal Amount,
    string? Description,
    Guid? AccountingJournalEntryId,
    string? AccountingJournalNumber,
    Guid? ReversalJournalEntryId,
    string? ReversalJournalNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReversedAt);
