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
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required AccountType Type { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Number { get; set; }
    public required DateOnly DocumentDate { get; set; }
    public string? Description { get; set; }
    public List<JournalLine> Lines { get; set; } = [];
    public JournalStatus Status { get; set; } = JournalStatus.Posted;
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
}

public sealed class JournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid JournalEntryId { get; set; }
    public required Guid AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
