using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Accounting;

public sealed class AccountingService(AppDbContext db)
{
    public async Task<IReadOnlyList<LedgerAccount>> GetAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<LedgerAccount> CreateAccountAsync(
        Guid companyId,
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Account code and name are required.");
        }

        var codeExists = await db.Accounts.AnyAsync(
            x => x.CompanyId == companyId && x.Code == code,
            cancellationToken);

        if (codeExists)
        {
            throw new InvalidOperationException("Account code already exists.");
        }

        if (request.ParentId is Guid parentId)
        {
            var parentExists = await db.Accounts.AnyAsync(
                x => x.Id == parentId && x.CompanyId == companyId,
                cancellationToken);

            if (!parentExists)
            {
                throw new ArgumentException(
                    "Parent account does not exist in this company.");
            }
        }

        var account = new LedgerAccount
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = request.Type,
            ParentId = request.ParentId
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return account;
    }

    public async Task<IReadOnlyList<JournalEntry>> GetJournalEntriesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<JournalEntry> PostJournalAsync(
        Guid companyId,
        Guid userId,
        CreateJournalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Number))
        {
            throw new ArgumentException("Journal number is required.");
        }

        if (request.Lines.Count < 2)
        {
            throw new ArgumentException(
                "A journal entry must contain at least two lines.");
        }

        if (request.Lines.Any(x =>
            x.Debit < 0 ||
            x.Credit < 0 ||
            (x.Debit > 0 && x.Credit > 0) ||
            (x.Debit == 0 && x.Credit == 0)))
        {
            throw new ArgumentException(
                "Each line must contain either a positive debit or a positive credit.");
        }

        var debit = request.Lines.Sum(x => x.Debit);
        var credit = request.Lines.Sum(x => x.Credit);

        if (debit <= 0 || debit != credit)
        {
            throw new ArgumentException("Journal entry is not balanced.");
        }

        var number = request.Number.Trim();

        var numberExists = await db.JournalEntries.AnyAsync(
            x => x.CompanyId == companyId && x.Number == number,
            cancellationToken);

        if (numberExists)
        {
            throw new InvalidOperationException("Journal number already exists.");
        }

        var accountIds = request.Lines
            .Select(x => x.AccountId)
            .Distinct()
            .ToArray();

        var validAccountIds = await db.Accounts
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                accountIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (validAccountIds.Length != accountIds.Length)
        {
            throw new ArgumentException(
                "One or more accounts are unavailable.");
        }

        var entry = new JournalEntry
        {
            CompanyId = companyId,
            Number = number,
            DocumentDate = request.DocumentDate,
            Description = request.Description?.Trim(),
            CreatedByUserId = userId,
            PostedAt = DateTimeOffset.UtcNow
        };

        entry.Lines = request.Lines.Select(x => new JournalLine
        {
            JournalEntryId = entry.Id,
            AccountId = x.AccountId,
            Description = x.Description?.Trim(),
            Debit = x.Debit,
            Credit = x.Credit
        }).ToList();

        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return entry;
    }

    public async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var journals = await db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted)
            .ToArrayAsync(cancellationToken);

        var rows = new List<TrialBalanceRow>();

        foreach (var account in accounts)
        {
            var lines = journals
                .SelectMany(x => x.Lines)
                .Where(x => x.AccountId == account.Id)
                .ToArray();

            var debit = lines.Sum(x => x.Debit);
            var credit = lines.Sum(x => x.Credit);

            rows.Add(new TrialBalanceRow(
                account.Id,
                account.Code,
                account.Name,
                debit,
                credit,
                debit - credit));
        }

        return rows;
    }

    public async Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(
        Guid companyId,
        Guid? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var accountLookup = await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var ordered = await db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted)
            .OrderBy(x => x.DocumentDate)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var balances = new Dictionary<Guid, decimal>();
        var rows = new List<GeneralLedgerRow>();

        foreach (var journal in ordered)
        {
            foreach (var line in journal.Lines)
            {
                if (!accountLookup.TryGetValue(line.AccountId, out var account))
                {
                    continue;
                }

                var current = balances.GetValueOrDefault(account.Id);
                current += line.Debit - line.Credit;
                balances[account.Id] = current;

                if (accountId is not null && account.Id != accountId.Value)
                {
                    continue;
                }

                rows.Add(new GeneralLedgerRow(
                    journal.Id,
                    journal.Number,
                    journal.DocumentDate,
                    account.Id,
                    account.Code,
                    account.Name,
                    line.Description ?? journal.Description,
                    line.Debit,
                    line.Credit,
                    current));
            }
        }

        return rows;
    }

    public async Task SeedDefaultAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (await db.Accounts.AnyAsync(
                x => x.CompanyId == companyId,
                cancellationToken))
        {
            return;
        }

        var defaults = new[]
        {
            ("1000", "Assets", AccountType.Asset),
            ("2000", "Liabilities", AccountType.Liability),
            ("3000", "Equity", AccountType.Equity),
            ("4000", "Revenue", AccountType.Revenue),
            ("5000", "Expenses", AccountType.Expense),
            ("1100", "Cash and Bank", AccountType.Asset),
            ("1200", "Accounts Receivable", AccountType.Asset),
            ("2100", "Accounts Payable", AccountType.Liability)
        };

        foreach (var item in defaults)
        {
            db.Accounts.Add(new LedgerAccount
            {
                CompanyId = companyId,
                Code = item.Item1,
                Name = item.Item2,
                Type = item.Item3
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
