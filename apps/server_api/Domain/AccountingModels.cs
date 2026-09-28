namespace ERPAccounting.Api.Domain;

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense
}

public enum JournalStatus
{
    Draft,
    Posted,
    Reversed
}

public sealed class LedgerAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public required AccountType Type { get; init; }
    public Guid? ParentId { get; init; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class JournalEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required string Number { get; init; }
    public required DateOnly DocumentDate { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<JournalLine> Lines { get; init; }
    public JournalStatus Status { get; set; } = JournalStatus.Posted;
    public required Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
}

public sealed class JournalLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AccountId { get; init; }
    public string? Description { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
}
