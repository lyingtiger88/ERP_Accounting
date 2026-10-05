using System.Data;
using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Treasury;

public sealed class TreasuryService(
    AppDbContext db,
    AccountingService accountingService,
    CurrencyAccountingService currencyService)
{
    public async Task<IReadOnlyList<TreasuryAccountView>> GetAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var accounts = (await db.TreasuryAccounts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .ToArrayAsync(cancellationToken))
            .OrderBy(x => x.Code)
            .ToArray();

        var result = new List<TreasuryAccountView>(accounts.Length);
        foreach (var account in accounts)
        {
            result.Add(await BuildAccountViewAsync(
                account,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<TreasuryAccountView> CreateAccountAsync(
        Guid companyId,
        CreateTreasuryAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (code.Length == 0 || name.Length == 0)
        {
            throw new ArgumentException(
                "Treasury account code and name are required.");
        }

        if (await db.TreasuryAccounts.AnyAsync(
                x => x.CompanyId == companyId && x.Code == code,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Treasury account code already exists.");
        }

        await ValidateLedgerAndCurrencyAsync(
            companyId,
            request.LedgerAccountId,
            request.CurrencyId,
            cancellationToken);

        var account = new TreasuryAccount
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = request.Type,
            LedgerAccountId = request.LedgerAccountId,
            CurrencyId = request.CurrencyId
        };

        db.TreasuryAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return await BuildAccountViewAsync(
            account,
            companyId,
            cancellationToken);
    }

    public async Task<TreasuryAccountView> UpdateAccountAsync(
        Guid companyId,
        Guid accountId,
        UpdateTreasuryAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await db.TreasuryAccounts
            .FirstOrDefaultAsync(
                x => x.Id == accountId && x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Treasury account does not exist in this company.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            throw new ArgumentException(
                "Treasury account code and name are required.");
        }

        if (await db.TreasuryAccounts.AnyAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.Code == code &&
                    x.Id != accountId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Treasury account code already exists.");
        }

        await ValidateLedgerAndCurrencyAsync(
            companyId,
            request.LedgerAccountId,
            request.CurrencyId,
            cancellationToken);

        account.Code = code;
        account.Name = name;
        account.Type = request.Type;
        account.LedgerAccountId = request.LedgerAccountId;
        account.CurrencyId = request.CurrencyId;
        account.IsActive = request.IsActive;
        account.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await BuildAccountViewAsync(
            account,
            companyId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<TreasuryTransactionView>>
        GetTransactionsAsync(
            Guid companyId,
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            throw new ArgumentException(
                "From date cannot be after to date.");
        }

        var query = db.TreasuryTransactions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (from is DateOnly fromDate)
        {
            query = query.Where(x => x.DocumentDate >= fromDate);
        }

        if (to is DateOnly toDate)
        {
            query = query.Where(x => x.DocumentDate <= toDate);
        }

        var rows = await query.ToArrayAsync(cancellationToken);
        var ordered = rows
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToArray();

        var result = new List<TreasuryTransactionView>(ordered.Length);
        foreach (var row in ordered)
        {
            result.Add(await BuildTransactionViewAsync(
                row,
                companyId,
                cancellationToken));
        }

        return result;
    }

    public async Task<TreasuryTransactionView> PostTransactionAsync(
        Guid companyId,
        Guid userId,
        CreateTreasuryTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new ArgumentException(
                "Treasury amount must be greater than zero.");
        }

        var fiscalYearExists = await db.FiscalYears.AnyAsync(
            x =>
                x.Id == request.FiscalYearId &&
                x.CompanyId == companyId,
            cancellationToken);

        if (!fiscalYearExists)
        {
            throw new ArgumentException(
                "Fiscal year does not exist in this company.");
        }

        TreasuryAccount? from = null;
        TreasuryAccount? to = null;

        if (request.FromTreasuryAccountId is Guid fromId)
        {
            from = await FindActiveTreasuryAccountAsync(
                companyId,
                fromId,
                cancellationToken);
        }

        if (request.ToTreasuryAccountId is Guid toId)
        {
            to = await FindActiveTreasuryAccountAsync(
                companyId,
                toId,
                cancellationToken);
        }

        LedgerAccount? counter = null;
        if (request.CounterAccountId is Guid counterId)
        {
            counter = await db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == counterId &&
                        x.CompanyId == companyId &&
                        x.IsActive,
                    cancellationToken)
                ?? throw new ArgumentException(
                    "Counter ledger account does not exist or is inactive.");
        }

        if (request.DetailAccountId is Guid detailId)
        {
            var detailExists = await db.DetailAccounts.AnyAsync(
                x =>
                    x.Id == detailId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken);

            if (!detailExists)
            {
                throw new ArgumentException(
                    "Detail account does not exist or is inactive.");
            }
        }

        TreasuryAccount currencyOwner;
        switch (request.Type)
        {
            case TreasuryTransactionType.Receipt:
                if (to is null || from is not null || counter is null)
                {
                    throw new ArgumentException(
                        "Receipt requires destination treasury and counter account.");
                }
                currencyOwner = to;
                break;

            case TreasuryTransactionType.Payment:
                if (from is null || to is not null || counter is null)
                {
                    throw new ArgumentException(
                        "Payment requires source treasury and counter account.");
                }
                currencyOwner = from;
                break;

            case TreasuryTransactionType.Transfer:
                if (from is null ||
                    to is null ||
                    counter is not null ||
                    from.Id == to.Id)
                {
                    throw new ArgumentException(
                        "Transfer requires different source and destination treasury accounts.");
                }

                if (from.CurrencyId != to.CurrencyId)
                {
                    throw new InvalidOperationException(
                        "Cross-currency treasury transfer is not supported yet.");
                }
                currencyOwner = from;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request.Type));
        }

        var currencyId = currencyOwner.CurrencyId;
        if (request.CurrencyId.HasValue &&
            request.CurrencyId != currencyId)
        {
            throw new ArgumentException(
                "Transaction currency must match the treasury account currency.");
        }

        var rate = 1m;
        if (currencyId is Guid foreignCurrencyId)
        {
            rate = request.ExchangeRate is > 0
                ? request.ExchangeRate.Value
                : await currencyService.ResolveAccountingRateAsync(
                    companyId,
                    foreignCurrencyId,
                    request.DocumentDate,
                    cancellationToken);
        }

        if (rate <= 0)
        {
            throw new ArgumentException(
                "Exchange rate must be greater than zero.");
        }

        var baseAmount = request.Amount * rate;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? request.Type switch
            {
                TreasuryTransactionType.Receipt => "دریافت وجه",
                TreasuryTransactionType.Payment => "پرداخت وجه",
                _ => "انتقال وجه"
            }
            : request.Description.Trim();

        var journalLines = new List<CreateJournalLineRequest>();

        if (request.Type == TreasuryTransactionType.Receipt)
        {
            journalLines.Add(CreateLine(
                to!.LedgerAccountId,
                description,
                baseAmount,
                0,
                null,
                currencyId,
                request.Amount,
                0,
                rate));

            journalLines.Add(CreateLine(
                counter!.Id,
                description,
                0,
                baseAmount,
                request.DetailAccountId,
                currencyId,
                0,
                request.Amount,
                rate));
        }
        else if (request.Type == TreasuryTransactionType.Payment)
        {
            journalLines.Add(CreateLine(
                counter!.Id,
                description,
                baseAmount,
                0,
                request.DetailAccountId,
                currencyId,
                request.Amount,
                0,
                rate));

            journalLines.Add(CreateLine(
                from!.LedgerAccountId,
                description,
                0,
                baseAmount,
                null,
                currencyId,
                0,
                request.Amount,
                rate));
        }
        else
        {
            journalLines.Add(CreateLine(
                to!.LedgerAccountId,
                description,
                baseAmount,
                0,
                null,
                currencyId,
                request.Amount,
                0,
                rate));

            journalLines.Add(CreateLine(
                from!.LedgerAccountId,
                description,
                0,
                baseAmount,
                null,
                currencyId,
                0,
                request.Amount,
                rate));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var number = await GenerateNumberAsync(
            companyId,
            request.FiscalYearId,
            cancellationToken);

        var journal = await accountingService
            .PostJournalWithinCurrentTransactionAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    request.DocumentDate,
                    description,
                    journalLines,
                    request.FiscalYearId),
                "POST_TREASURY_TRANSACTION",
                cancellationToken);

        var entity = new TreasuryTransaction
        {
            CompanyId = companyId,
            FiscalYearId = request.FiscalYearId,
            Number = number,
            DocumentDate = request.DocumentDate,
            Type = request.Type,
            FromTreasuryAccountId = from?.Id,
            ToTreasuryAccountId = to?.Id,
            CounterAccountId = counter?.Id,
            DetailAccountId = request.DetailAccountId,
            CurrencyId = currencyId,
            ExchangeRate = rate,
            Amount = request.Amount,
            Description = description,
            AccountingJournalEntryId = journal.Id,
            CreatedByUserId = userId
        };

        db.TreasuryTransactions.Add(entity);
        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = "TreasuryTransaction",
            EntityId = entity.Id,
            Action = "TREASURY_POST",
            PayloadJson =
                $@"{{""number"":""{number}"",""type"":""{request.Type}"",""journalNumber"":""{journal.Number}""}}"
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildTransactionViewAsync(
            entity,
            companyId,
            cancellationToken);
    }

    private async Task ValidateLedgerAndCurrencyAsync(
        Guid companyId,
        Guid ledgerAccountId,
        Guid? currencyId,
        CancellationToken cancellationToken)
    {
        var ledger = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == ledgerAccountId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Ledger account does not exist or is inactive.");

        if (ledger.Type != AccountType.Asset)
        {
            throw new ArgumentException(
                "Treasury ledger account must be an asset account.");
        }

        if (currencyId is Guid id)
        {
            var currencyExists = await db.Currencies.AnyAsync(
                x =>
                    x.Id == id &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken);

            if (!currencyExists)
            {
                throw new ArgumentException(
                    "Treasury currency does not exist or is inactive.");
            }
        }
    }

    private async Task<TreasuryAccount> FindActiveTreasuryAccountAsync(
        Guid companyId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return await db.TreasuryAccounts
            .FirstOrDefaultAsync(
                x =>
                    x.Id == accountId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Treasury account does not exist or is inactive.");
    }

    private static CreateJournalLineRequest CreateLine(
        Guid accountId,
        string description,
        decimal debit,
        decimal credit,
        Guid? detailAccountId,
        Guid? currencyId,
        decimal foreignDebit,
        decimal foreignCredit,
        decimal exchangeRate)
    {
        return new CreateJournalLineRequest(
            accountId,
            description,
            debit,
            credit,
            detailAccountId,
            CurrencyId: currencyId,
            ForeignDebit: currencyId.HasValue ? foreignDebit : null,
            ForeignCredit: currencyId.HasValue ? foreignCredit : null,
            ExchangeRate: currencyId.HasValue ? exchangeRate : null);
    }

    private async Task<string> GenerateNumberAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var fiscalYear = await db.FiscalYears
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken);

        var count = await db.TreasuryTransactions.CountAsync(
            x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId,
            cancellationToken);

        return $"TR-{fiscalYear.PersianYear}-{count + 1:000000}";
    }

    private async Task<TreasuryAccountView> BuildAccountViewAsync(
        TreasuryAccount account,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var ledger = await db.Accounts
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.Id == account.LedgerAccountId &&
                    x.CompanyId == companyId,
                cancellationToken);

        var currencyCode = await GetCurrencyCodeAsync(
            companyId,
            account.CurrencyId,
            cancellationToken);

        return new TreasuryAccountView(
            account.Id,
            account.Code,
            account.Name,
            account.Type,
            account.LedgerAccountId,
            ledger.Code,
            ledger.Name,
            account.CurrencyId,
            currencyCode,
            account.IsActive);
    }

    private async Task<TreasuryTransactionView> BuildTransactionViewAsync(
        TreasuryTransaction row,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var treasuryIds = new[]
            {
                row.FromTreasuryAccountId,
                row.ToTreasuryAccountId
            }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        var treasury = await db.TreasuryAccounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                treasuryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        LedgerAccount? counter = null;
        if (row.CounterAccountId is Guid counterId)
        {
            counter = await db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == counterId,
                    cancellationToken);
        }

        string? detailName = null;
        if (row.DetailAccountId is Guid detailId)
        {
            detailName = await db.DetailAccounts
                .AsNoTracking()
                .Where(x => x.Id == detailId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? journalNumber = null;
        if (row.AccountingJournalEntryId is Guid journalId)
        {
            journalNumber = await db.JournalEntries
                .AsNoTracking()
                .Where(x => x.Id == journalId)
                .Select(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? reversalNumber = null;
        if (row.ReversalJournalEntryId is Guid reversalId)
        {
            reversalNumber = await db.JournalEntries
                .AsNoTracking()
                .Where(x => x.Id == reversalId)
                .Select(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new TreasuryTransactionView(
            row.Id,
            row.FiscalYearId,
            row.Number,
            row.DocumentDate,
            row.Type,
            row.Status,
            row.FromTreasuryAccountId,
            row.FromTreasuryAccountId is Guid fromId &&
                treasury.TryGetValue(fromId, out var fromAccount)
                ? fromAccount.Name
                : null,
            row.ToTreasuryAccountId,
            row.ToTreasuryAccountId is Guid toId &&
                treasury.TryGetValue(toId, out var toAccount)
                ? toAccount.Name
                : null,
            row.CounterAccountId,
            counter?.Code,
            counter?.Name,
            row.DetailAccountId,
            detailName,
            row.CurrencyId,
            await GetCurrencyCodeAsync(
                companyId,
                row.CurrencyId,
                cancellationToken),
            row.ExchangeRate,
            row.Amount,
            row.Description,
            row.AccountingJournalEntryId,
            journalNumber,
            row.ReversalJournalEntryId,
            reversalNumber,
            row.CreatedAt,
            row.ReversedAt);
    }

    private async Task<string> GetCurrencyCodeAsync(
        Guid companyId,
        Guid? currencyId,
        CancellationToken cancellationToken)
    {
        if (currencyId is Guid id)
        {
            return await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.Id == id &&
                    x.CompanyId == companyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(cancellationToken)
                ?? "BASE";
        }

        return await db.Currencies
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsBase)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken)
            ?? "IRR";
    }
}
