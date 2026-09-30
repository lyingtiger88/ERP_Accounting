using System.Data;
using System.Globalization;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Accounting;

public sealed class AccountingService(AppDbContext db)
{
    public async Task<IReadOnlyList<AccountView>> GetAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var lookup = accounts.ToDictionary(x => x.Id);

        return accounts
            .Select(account =>
            {
                var level = ResolveLevel(account, lookup);
                var nature = ResolveNature(account.Type);

                return new AccountView(
                    account.Id,
                    account.CompanyId,
                    account.Code,
                    account.Name,
                    account.Type,
                    account.ParentId,
                    account.IsActive,
                    level,
                    nature,
                    GetLevelTitle(level),
                    nature == AccountNature.Debit ? "بدهکار" : "بستانکار");
            })
            .ToArray();
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

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var number = request.Number?.Trim();

        if (string.IsNullOrWhiteSpace(number))
        {
            number = await GenerateJournalNumberAsync(
                companyId,
                request.DocumentDate,
                cancellationToken);
        }
        else
        {
            var numberExists = await db.JournalEntries.AnyAsync(
                x => x.CompanyId == companyId && x.Number == number,
                cancellationToken);

            if (numberExists)
            {
                throw new InvalidOperationException(
                    "Journal number already exists.");
            }
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
        await transaction.CommitAsync(cancellationToken);

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
        var seeds = new[]
        {
            new SeedAccount("1000", "دارایی‌ها", AccountType.Asset, null),
            new SeedAccount("1100", "نقد و بانک", AccountType.Asset, "1000"),
            new SeedAccount("1110", "صندوق", AccountType.Asset, "1100"),
            new SeedAccount("1120", "بانک‌ها", AccountType.Asset, "1100"),
            new SeedAccount("1200", "حساب‌ها و اسناد دریافتنی", AccountType.Asset, "1000"),
            new SeedAccount("1300", "موجودی مواد و کالا", AccountType.Asset, "1000"),
            new SeedAccount("1400", "پیش‌پرداخت‌ها", AccountType.Asset, "1000"),
            new SeedAccount("1500", "دارایی‌های ثابت مشهود", AccountType.Asset, "1000"),

            new SeedAccount("2000", "بدهی‌ها", AccountType.Liability, null),
            new SeedAccount("2100", "حساب‌ها و اسناد پرداختنی", AccountType.Liability, "2000"),
            new SeedAccount("2200", "مالیات و عوارض پرداختنی", AccountType.Liability, "2000"),
            new SeedAccount("2300", "حقوق و دستمزد پرداختنی", AccountType.Liability, "2000"),

            new SeedAccount("3000", "حقوق مالکانه", AccountType.Equity, null),
            new SeedAccount("3100", "سرمایه", AccountType.Equity, "3000"),
            new SeedAccount("3200", "سود و زیان انباشته", AccountType.Equity, "3000"),

            new SeedAccount("4000", "درآمدها", AccountType.Revenue, null),
            new SeedAccount("4100", "فروش کالا و خدمات", AccountType.Revenue, "4000"),
            new SeedAccount("4200", "سایر درآمدها", AccountType.Revenue, "4000"),

            new SeedAccount("5000", "بهای تمام‌شده و هزینه‌ها", AccountType.Expense, null),
            new SeedAccount("5100", "بهای تمام‌شده فروش", AccountType.Expense, "5000"),
            new SeedAccount("5200", "هزینه‌های اداری و عمومی", AccountType.Expense, "5000"),
            new SeedAccount("5300", "هزینه‌های فروش و توزیع", AccountType.Expense, "5000"),
            new SeedAccount("5400", "هزینه‌های مالی", AccountType.Expense, "5000")
        };

        var seedCodes = seeds.Select(x => x.Code).ToArray();

        var existing = await db.Accounts
            .Where(x =>
                x.CompanyId == companyId &&
                seedCodes.Contains(x.Code))
            .ToArrayAsync(cancellationToken);

        var byCode = existing.ToDictionary(x => x.Code);

        foreach (var seed in seeds)
        {
            if (!byCode.TryGetValue(seed.Code, out var account))
            {
                account = new LedgerAccount
                {
                    CompanyId = companyId,
                    Code = seed.Code,
                    Name = seed.Name,
                    Type = seed.Type
                };

                db.Accounts.Add(account);
                byCode[seed.Code] = account;
            }

            account.Name = seed.Name;
            account.Type = seed.Type;
            account.IsActive = true;
            account.ParentId = seed.ParentCode is null
                ? null
                : byCode[seed.ParentCode].Id;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GenerateJournalNumberAsync(
        Guid companyId,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var persianCalendar = new PersianCalendar();
        var dateTime = documentDate.ToDateTime(TimeOnly.MinValue);
        var fiscalYear = persianCalendar.GetYear(dateTime);
        var prefix = fiscalYear.ToString(CultureInfo.InvariantCulture) + "/";

        var existingNumbers = await db.JournalEntries
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Number.StartsWith(prefix))
            .Select(x => x.Number)
            .ToArrayAsync(cancellationToken);

        var maxSequence = 0;

        foreach (var existingNumber in existingNumbers)
        {
            var suffix = existingNumber[prefix.Length..];

            if (int.TryParse(
                    suffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var sequence))
            {
                maxSequence = Math.Max(maxSequence, sequence);
            }
        }

        return prefix +
            (maxSequence + 1).ToString("000000", CultureInfo.InvariantCulture);
    }

    private static AccountNature ResolveNature(AccountType type)
    {
        return type is AccountType.Asset or AccountType.Expense
            ? AccountNature.Debit
            : AccountNature.Credit;
    }

    private static AccountLevel ResolveLevel(
        LedgerAccount account,
        IReadOnlyDictionary<Guid, LedgerAccount> lookup)
    {
        var depth = 1;
        var current = account;
        var visited = new HashSet<Guid> { current.Id };

        while (current.ParentId is Guid parentId &&
               lookup.TryGetValue(parentId, out var parent) &&
               visited.Add(parent.Id))
        {
            depth++;
            current = parent;

            if (depth >= (int)AccountLevel.Detail)
            {
                break;
            }
        }

        return (AccountLevel)Math.Clamp(
            depth,
            (int)AccountLevel.Group,
            (int)AccountLevel.Detail);
    }

    private static string GetLevelTitle(AccountLevel level)
    {
        return level switch
        {
            AccountLevel.Group => "گروه",
            AccountLevel.General => "کل",
            AccountLevel.Subsidiary => "معین",
            AccountLevel.Detail => "تفصیلی",
            _ => "نامشخص"
        };
    }

    private sealed record SeedAccount(
        string Code,
        string Name,
        AccountType Type,
        string? ParentCode);
}
