using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;

namespace ERPAccounting.Api.Accounting;

public sealed class AccountingService(InMemoryStore store)
{
    public IReadOnlyList<LedgerAccount> GetAccounts(Guid companyId)
    {
        lock (store.Gate)
        {
            return store.Accounts.Values
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Code)
                .ToArray();
        }
    }

    public LedgerAccount CreateAccount(Guid companyId, CreateAccountRequest request)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Account code and name are required.");
        }

        lock (store.Gate)
        {
            if (store.Accounts.Values.Any(x =>
                x.CompanyId == companyId &&
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Account code already exists.");
            }

            if (request.ParentId is Guid parentId)
            {
                var parent = store.Accounts.GetValueOrDefault(parentId);
                if (parent is null || parent.CompanyId != companyId)
                {
                    throw new ArgumentException("Parent account does not exist in this company.");
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

            store.Accounts[account.Id] = account;
            return account;
        }
    }

    public IReadOnlyList<JournalEntry> GetJournalEntries(Guid companyId)
    {
        lock (store.Gate)
        {
            return store.JournalEntries.Values
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAt)
                .ToArray();
        }
    }

    public JournalEntry PostJournal(Guid companyId, Guid userId, CreateJournalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Number))
        {
            throw new ArgumentException("Journal number is required.");
        }

        if (request.Lines.Count < 2)
        {
            throw new ArgumentException("A journal entry must contain at least two lines.");
        }

        if (request.Lines.Any(x =>
            x.Debit < 0 ||
            x.Credit < 0 ||
            (x.Debit > 0 && x.Credit > 0) ||
            (x.Debit == 0 && x.Credit == 0)))
        {
            throw new ArgumentException("Each line must contain either a positive debit or a positive credit.");
        }

        var debit = request.Lines.Sum(x => x.Debit);
        var credit = request.Lines.Sum(x => x.Credit);

        if (debit <= 0 || debit != credit)
        {
            throw new ArgumentException("Journal entry is not balanced.");
        }

        lock (store.Gate)
        {
            if (store.JournalEntries.Values.Any(x =>
                x.CompanyId == companyId &&
                string.Equals(x.Number, request.Number.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Journal number already exists.");
            }

            foreach (var line in request.Lines)
            {
                var account = store.Accounts.GetValueOrDefault(line.AccountId);
                if (account is null || account.CompanyId != companyId || !account.IsActive)
                {
                    throw new ArgumentException($"Account {line.AccountId} is unavailable.");
                }
            }

            var entry = new JournalEntry
            {
                CompanyId = companyId,
                Number = request.Number.Trim(),
                DocumentDate = request.DocumentDate,
                Description = request.Description?.Trim(),
                CreatedByUserId = userId,
                PostedAt = DateTimeOffset.UtcNow,
                Lines = request.Lines.Select(x => new JournalLine
                {
                    AccountId = x.AccountId,
                    Description = x.Description?.Trim(),
                    Debit = x.Debit,
                    Credit = x.Credit
                }).ToArray()
            };

            store.JournalEntries[entry.Id] = entry;
            return entry;
        }
    }


    public IReadOnlyList<TrialBalanceRow> GetTrialBalance(Guid companyId)
    {
        lock (store.Gate)
        {
            var journals = store.JournalEntries.Values
                .Where(x => x.CompanyId == companyId && x.Status == JournalStatus.Posted)
                .ToArray();

            var rows = new List<TrialBalanceRow>();

            foreach (var account in store.Accounts.Values
                         .Where(x => x.CompanyId == companyId)
                         .OrderBy(x => x.Code))
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
    }

    public IReadOnlyList<GeneralLedgerRow> GetGeneralLedger(
        Guid companyId,
        Guid? accountId = null)
    {
        lock (store.Gate)
        {
            var accountLookup = store.Accounts.Values
                .Where(x => x.CompanyId == companyId)
                .ToDictionary(x => x.Id);

            var ordered = store.JournalEntries.Values
                .Where(x => x.CompanyId == companyId && x.Status == JournalStatus.Posted)
                .OrderBy(x => x.DocumentDate)
                .ThenBy(x => x.CreatedAt)
                .ToArray();

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
    }

    public void SeedDefaultAccounts(Guid companyId)
    {
        lock (store.Gate)
        {
            if (store.Accounts.Values.Any(x => x.CompanyId == companyId))
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
                var account = new LedgerAccount
                {
                    CompanyId = companyId,
                    Code = item.Item1,
                    Name = item.Item2,
                    Type = item.Item3
                };
                store.Accounts[account.Id] = account;
            }
        }
    }
}
