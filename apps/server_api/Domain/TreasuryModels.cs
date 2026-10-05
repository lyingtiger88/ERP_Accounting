namespace ERPAccounting.Api.Domain;

public enum TreasuryAccountType
{
    Cashbox,
    Bank
}

public enum TreasuryTransactionType
{
    Receipt,
    Payment,
    Transfer
}

public enum TreasuryTransactionStatus
{
    Posted,
    Reversed
}

public sealed class TreasuryAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public TreasuryAccountType Type { get; set; }
    public required Guid LedgerAccountId { get; set; }
    public Guid? CurrencyId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TreasuryTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid FiscalYearId { get; set; }
    public required string Number { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public TreasuryTransactionType Type { get; set; }
    public TreasuryTransactionStatus Status { get; set; } =
        TreasuryTransactionStatus.Posted;
    public Guid? FromTreasuryAccountId { get; set; }
    public Guid? ToTreasuryAccountId { get; set; }
    public Guid? CounterAccountId { get; set; }
    public Guid? DetailAccountId { get; set; }
    public Guid? CurrencyId { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public Guid? AccountingJournalEntryId { get; set; }
    public Guid? ReversalJournalEntryId { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReversedAt { get; set; }
}
