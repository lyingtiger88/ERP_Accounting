using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Contracts;

public sealed record CreateAccountRequest(
    string Code,
    string Name,
    AccountType Type,
    Guid? ParentId);

public sealed record CreateJournalLineRequest(
    Guid AccountId,
    string? Description,
    decimal Debit,
    decimal Credit);

public sealed record CreateJournalRequest(
    string Number,
    DateOnly DocumentDate,
    string? Description,
    IReadOnlyList<CreateJournalLineRequest> Lines);
