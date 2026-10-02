using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Contracts;

public sealed record CreateAccountRequest(
    string Code,
    string Name,
    AccountType Type,
    Guid? ParentId);

public sealed record AccountView(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    AccountType Type,
    Guid? ParentId,
    bool IsActive,
    AccountLevel Level,
    AccountNature Nature,
    string LevelTitle,
    string NatureTitle,
    bool IsPostable);

public sealed record FiscalYearView(
    Guid Id,
    Guid CompanyId,
    string Name,
    int PersianYear,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsDefault,
    bool IsClosed,
    bool IsFinalized,
    string? ClosingJournalNumber,
    decimal? NetResult);

public sealed record CreateFiscalYearRequest(
    string Name,
    int PersianYear,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsDefault);

public sealed record SetFiscalYearStateRequest(
    bool IsClosed);

public sealed record SetFiscalPeriodStateRequest(
    bool IsClosed);

public sealed record CreateCostCenterRequest(
    string Code,
    string Name);

public sealed record CreateAccountingProjectRequest(
    string Code,
    string Name);

public sealed record CreateDetailAccountRequest(
    string Code,
    string Name,
    DetailAccountType Type,
    string? NationalId);

public sealed record CreateJournalLineRequest(
    Guid AccountId,
    string? Description,
    decimal Debit,
    decimal Credit,
    Guid? DetailAccountId = null,
    Guid? CostCenterId = null,
    Guid? ProjectId = null,
    Guid? CurrencyId = null,
    decimal? ForeignDebit = null,
    decimal? ForeignCredit = null,
    decimal? ExchangeRate = null);

public sealed record CreateJournalRequest(
    string? Number,
    DateOnly DocumentDate,
    string? Description,
    IReadOnlyList<CreateJournalLineRequest> Lines,
    Guid? FiscalYearId = null);


public sealed record SyncJournalRequest(
    string ChangeId,
    string LocalDocumentId,
    Guid? FiscalYearId,
    DateOnly DocumentDate,
    string? Description,
    IReadOnlyList<CreateJournalLineRequest> Lines);

public sealed record SyncJournalResponse(
    Guid JournalEntryId,
    string Number,
    JournalStatus Status,
    DateTimeOffset? PostedAt,
    bool Duplicate);


public sealed record ServerJournalLineView(
    Guid AccountId,
    Guid? DetailAccountId,
    Guid? CostCenterId,
    Guid? ProjectId,
    string? Description,
    decimal Debit,
    decimal Credit,
    Guid? CurrencyId = null,
    decimal? ForeignDebit = null,
    decimal? ForeignCredit = null,
    decimal? ExchangeRate = null);

public sealed record ServerJournalChangeView(
    long Cursor,
    Guid JournalEntryId,
    string Number,
    Guid? FiscalYearId,
    DateOnly DocumentDate,
    string? Description,
    JournalStatus Status,
    DateTimeOffset? PostedAt,
    Guid? ReversalOfJournalEntryId,
    IReadOnlyList<ServerJournalLineView> Lines);

public sealed record ServerJournalPullResponse(
    long NextCursor,
    bool HasMore,
    IReadOnlyList<ServerJournalChangeView> Changes);


public sealed record DetailAccountView(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    DetailAccountType Type,
    string? NationalId,
    bool IsActive,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record UpdateDetailAccountRequest(
    string Code,
    string Name,
    DetailAccountType Type,
    string? NationalId,
    bool IsActive,
    long BaseRevision);

public sealed record SyncDetailAccountRequest(
    string ChangeId,
    Guid EntityId,
    string Code,
    string Name,
    DetailAccountType Type,
    string? NationalId,
    bool IsActive,
    long BaseRevision);

public sealed record DetailAccountSyncConflict(
    DetailAccountView? Server,
    long BaseRevision);

public sealed record SyncDetailAccountResponse(
    string Outcome,
    DetailAccountView? Entity,
    DetailAccountSyncConflict? Conflict,
    bool Duplicate);


public sealed record DetailAccountServerChangeView(
    long Cursor,
    DetailAccountView Entity);

public sealed record DetailAccountPullResponse(
    long NextCursor,
    bool HasMore,
    IReadOnlyList<DetailAccountServerChangeView> Changes);


public sealed record ReverseJournalRequest(
    DateOnly DocumentDate,
    string Reason,
    Guid? FiscalYearId = null);

public sealed record ReverseJournalResponse(
    Guid OriginalJournalEntryId,
    Guid ReversalJournalEntryId,
    string ReversalNumber,
    DateTimeOffset? PostedAt);

public sealed record AccountingAuditView(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    string? Reason,
    Guid UserId,
    string UserDisplayName,
    DateTimeOffset CreatedAt);


public sealed record FinalizeFiscalYearRequest(
    Guid RetainedEarningsAccountId);

public sealed record FinalizeFiscalYearResponse(
    Guid FiscalYearId,
    Guid? ClosingJournalEntryId,
    string? ClosingJournalNumber,
    decimal NetResult,
    bool AlreadyFinalized);

public sealed record ReopenFinalizedFiscalYearResponse(
    Guid FiscalYearId,
    Guid? ReversalJournalEntryId,
    string? ReversalJournalNumber);
