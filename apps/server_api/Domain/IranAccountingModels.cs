namespace ERPAccounting.Api.Domain;

public enum DetailAccountType
{
    Customer,
    Supplier,
    Person,
    Employee,
    Bank,
    Government,
    Other
}

public sealed class FiscalYear
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Name { get; set; }
    public required int PersianYear { get; set; }
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FiscalPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid FiscalYearId { get; set; }
    public required int PeriodNumber { get; set; }
    public required string Name { get; set; }
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FiscalYearClosing
{
    public required Guid FiscalYearId { get; set; }
    public required Guid CompanyId { get; set; }
    public Guid? ClosingJournalEntryId { get; set; }
    public required Guid RetainedEarningsAccountId { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public decimal NetResult { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DetailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required DetailAccountType Type { get; set; }
    public string? NationalId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JournalEntryFiscalYear
{
    public required Guid JournalEntryId { get; set; }
    public required Guid FiscalYearId { get; set; }
}

public sealed class JournalLineDimension
{
    public required Guid JournalLineId { get; set; }
    public Guid? DetailAccountId { get; set; }
}


public sealed class JournalSyncReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string ChangeId { get; set; }
    public required string LocalDocumentId { get; set; }
    public required Guid JournalEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}


public sealed class JournalServerChange
{
    public long Sequence { get; set; }
    public required Guid CompanyId { get; set; }
    public required Guid JournalEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}


public sealed class DetailAccountSyncState
{
    public required Guid DetailAccountId { get; set; }
    public required Guid CompanyId { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DetailAccountSyncReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string ChangeId { get; set; }
    public required Guid DetailAccountId { get; set; }
    public required long AppliedRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DetailAccountServerChange
{
    public long Sequence { get; set; }
    public required Guid CompanyId { get; set; }
    public required Guid DetailAccountId { get; set; }
    public required long Revision { get; set; }
    public required string Operation { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}


public sealed class JournalReversalLink
{
    public required Guid OriginalJournalEntryId { get; set; }
    public required Guid ReversalJournalEntryId { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AccountingAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid UserId { get; set; }
    public required string EntityType { get; set; }
    public required Guid EntityId { get; set; }
    public required string Action { get; set; }
    public string? Reason { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
