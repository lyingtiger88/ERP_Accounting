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
