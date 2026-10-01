using System.Data;
using System.Globalization;
using System.Text.Json;
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
        var parentIds = accounts
            .Where(x => x.ParentId.HasValue)
            .Select(x => x.ParentId!.Value)
            .ToHashSet();

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
                    nature == AccountNature.Debit ? "بدهکار" : "بستانکار",
                    !parentIds.Contains(account.Id));
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<FiscalYear>> GetFiscalYearsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.FiscalYears
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartDate)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FiscalPeriod>> GetFiscalPeriodsAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        var yearExists = await db.FiscalYears.AnyAsync(
            x =>
                x.Id == fiscalYearId &&
                x.CompanyId == companyId,
            cancellationToken);

        if (!yearExists)
        {
            throw new ArgumentException(
                "Fiscal year does not exist in this company.");
        }

        return await db.FiscalPeriods
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId)
            .OrderBy(x => x.PeriodNumber)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FiscalPeriod>> EnsureStandardFiscalPeriodsAsync(
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        var fiscalYear = await db.FiscalYears
            .FirstOrDefaultAsync(
                x =>
                    x.Id == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal year does not exist in this company.");

        if (!IsStandardPersianFiscalYear(fiscalYear))
        {
            throw new InvalidOperationException(
                "Standard monthly periods require a full Solar Hijri fiscal year.");
        }

        return await EnsureStandardFiscalPeriodsCoreAsync(
            companyId,
            fiscalYear,
            cancellationToken);
    }

    public async Task<FiscalPeriod> SetFiscalPeriodClosedAsync(
        Guid companyId,
        Guid userId,
        Guid periodId,
        bool isClosed,
        CancellationToken cancellationToken = default)
    {
        var period = await db.FiscalPeriods
            .FirstOrDefaultAsync(
                x =>
                    x.Id == periodId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal period does not exist in this company.");

        period.IsClosed = isClosed;

        AddAuditLog(
            companyId,
            userId,
            "FiscalPeriod",
            period.Id,
            isClosed
                ? "FISCAL_PERIOD_CLOSE"
                : "FISCAL_PERIOD_REOPEN",
            null,
            new
            {
                period.FiscalYearId,
                period.PeriodNumber,
                period.Name,
                period.StartDate,
                period.EndDate
            });

        await db.SaveChangesAsync(cancellationToken);
        return period;
    }

    public async Task<FiscalYear> CreateFiscalYearAsync(
        Guid companyId,
        CreateFiscalYearRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Fiscal year name is required.");
        }

        if (request.PersianYear is < 1200 or > 1700)
        {
            throw new ArgumentException("Persian fiscal year is outside the supported range.");
        }

        if (request.EndDate < request.StartDate)
        {
            throw new ArgumentException("Fiscal year end date cannot be before start date.");
        }

        var overlaps = await db.FiscalYears.AnyAsync(
            x =>
                x.CompanyId == companyId &&
                request.StartDate <= x.EndDate &&
                request.EndDate >= x.StartDate,
            cancellationToken);

        if (overlaps)
        {
            throw new InvalidOperationException(
                "Fiscal year overlaps an existing fiscal year.");
        }

        if (request.IsDefault)
        {
            var defaults = await db.FiscalYears
                .Where(x => x.CompanyId == companyId && x.IsDefault)
                .ToArrayAsync(cancellationToken);

            foreach (var current in defaults)
            {
                current.IsDefault = false;
            }
        }

        var fiscalYear = new FiscalYear
        {
            CompanyId = companyId,
            Name = name,
            PersianYear = request.PersianYear,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsDefault = request.IsDefault
        };

        db.FiscalYears.Add(fiscalYear);
        await db.SaveChangesAsync(cancellationToken);

        if (IsStandardPersianFiscalYear(fiscalYear))
        {
            await EnsureStandardFiscalPeriodsCoreAsync(
                companyId,
                fiscalYear,
                cancellationToken);
        }

        return fiscalYear;
    }

    public async Task<FiscalYear> SetFiscalYearClosedAsync(
        Guid companyId,
        Guid userId,
        Guid fiscalYearId,
        bool isClosed,
        CancellationToken cancellationToken = default)
    {
        var fiscalYear = await db.FiscalYears.FirstOrDefaultAsync(
            x => x.Id == fiscalYearId && x.CompanyId == companyId,
            cancellationToken);

        if (fiscalYear is null)
        {
            throw new ArgumentException(
                "Fiscal year does not exist in this company.");
        }

        var finalization = await db.FiscalYearClosings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.FiscalYearId == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken);

        if (isClosed)
        {
            var hasOpenPeriod = await db.FiscalPeriods.AnyAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.FiscalYearId == fiscalYearId &&
                    !x.IsClosed,
                cancellationToken);

            if (hasOpenPeriod)
            {
                throw new InvalidOperationException(
                    "All fiscal periods must be closed before closing the fiscal year.");
            }

            if (finalization is null &&
                await TemporaryAccountsHaveBalanceAsync(
                    companyId,
                    fiscalYear,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "Revenue or expense accounts still have balances. Run fiscal-year finalization first.");
            }
        }
        else if (finalization is not null)
        {
            throw new InvalidOperationException(
                "A finalized fiscal year must be reopened through the finalization-reopen workflow.");
        }

        fiscalYear.IsClosed = isClosed;

        AddAuditLog(
            companyId,
            userId,
            "FiscalYear",
            fiscalYear.Id,
            isClosed ? "FISCAL_YEAR_CLOSE" : "FISCAL_YEAR_REOPEN",
            null,
            new
            {
                fiscalYear.Name,
                fiscalYear.PersianYear,
                fiscalYear.StartDate,
                fiscalYear.EndDate
            });

        await db.SaveChangesAsync(cancellationToken);

        return fiscalYear;
    }

    public async Task<FinalizeFiscalYearResponse> FinalizeFiscalYearAsync(
        Guid companyId,
        Guid userId,
        Guid fiscalYearId,
        FinalizeFiscalYearRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var fiscalYear = await db.FiscalYears
            .FirstOrDefaultAsync(
                x =>
                    x.Id == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal year does not exist in this company.");

        var existing = await db.FiscalYearClosings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.FiscalYearId == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken);

        if (existing is not null)
        {
            string? existingNumber = null;

            if (existing.ClosingJournalEntryId is Guid journalId)
            {
                existingNumber = await db.JournalEntries
                    .AsNoTracking()
                    .Where(x => x.Id == journalId)
                    .Select(x => x.Number)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return new FinalizeFiscalYearResponse(
                fiscalYear.Id,
                existing.ClosingJournalEntryId,
                existingNumber,
                existing.NetResult,
                true);
        }

        if (fiscalYear.IsClosed)
        {
            throw new InvalidOperationException(
                "Closed fiscal year has no finalization record. Reopen it before running finalization.");
        }

        var retainedAccount = await db.Accounts
            .FirstOrDefaultAsync(
                x =>
                    x.Id == request.RetainedEarningsAccountId &&
                    x.CompanyId == companyId &&
                    x.IsActive,
                cancellationToken)
            ?? throw new ArgumentException(
                "Retained earnings account does not exist or is inactive.");

        if (retainedAccount.Type != AccountType.Equity)
        {
            throw new ArgumentException(
                "Retained earnings account must be an equity account.");
        }

        var hasChildren = await db.Accounts.AnyAsync(
            x =>
                x.CompanyId == companyId &&
                x.ParentId == retainedAccount.Id,
            cancellationToken);

        if (hasChildren)
        {
            throw new ArgumentException(
                "Retained earnings account must be a postable leaf account.");
        }

        var temporaryAccounts = await db.Accounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.Type == AccountType.Revenue ||
                 x.Type == AccountType.Expense))
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var temporaryIds = temporaryAccounts
            .Select(x => x.Id)
            .ToHashSet();

        var journals = await db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted &&
                x.DocumentDate >= fiscalYear.StartDate &&
                x.DocumentDate <= fiscalYear.EndDate)
            .ToArrayAsync(cancellationToken);

        var closingLines = new List<CreateJournalLineRequest>();
        decimal revenueTotal = 0;
        decimal expenseTotal = 0;

        foreach (var account in temporaryAccounts)
        {
            var lines = journals
                .SelectMany(x => x.Lines)
                .Where(x => x.AccountId == account.Id)
                .ToArray();

            var debitTotal = lines.Sum(x => x.Debit);
            var creditTotal = lines.Sum(x => x.Credit);
            var balance = debitTotal - creditTotal;

            if (account.Type == AccountType.Revenue)
            {
                revenueTotal += creditTotal - debitTotal;
            }
            else
            {
                expenseTotal += debitTotal - creditTotal;
            }

            if (balance == 0)
            {
                continue;
            }

            closingLines.Add(
                balance > 0
                    ? new CreateJournalLineRequest(
                        account.Id,
                        $"بستن حساب {account.Code} - {account.Name}",
                        0,
                        balance)
                    : new CreateJournalLineRequest(
                        account.Id,
                        $"بستن حساب {account.Code} - {account.Name}",
                        -balance,
                        0));
        }

        var netResult = revenueTotal - expenseTotal;
        Guid? closingJournalId = null;
        string? closingJournalNumber = null;

        if (closingLines.Count > 0)
        {
            var debit = closingLines.Sum(x => x.Debit);
            var credit = closingLines.Sum(x => x.Credit);

            if (debit > credit)
            {
                closingLines.Add(new CreateJournalLineRequest(
                    retainedAccount.Id,
                    $"انتقال نتیجه عملکرد سال مالی {fiscalYear.Name}",
                    0,
                    debit - credit));
            }
            else if (credit > debit)
            {
                closingLines.Add(new CreateJournalLineRequest(
                    retainedAccount.Id,
                    $"انتقال نتیجه عملکرد سال مالی {fiscalYear.Name}",
                    credit - debit,
                    0));
            }

            var closingJournal = await CreatePostedJournalCoreAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    fiscalYear.EndDate,
                    $"سند بستن حساب‌های موقت {fiscalYear.Name}",
                    closingLines,
                    fiscalYear.Id),
                cancellationToken,
                enforceFiscalControls: false);

            closingJournalId = closingJournal.Id;
            closingJournalNumber = closingJournal.Number;

            AddAuditLog(
                companyId,
                userId,
                "JournalEntry",
                closingJournal.Id,
                "YEAR_END_CLOSING_POST",
                null,
                new
                {
                    FiscalYearId = fiscalYear.Id,
                    fiscalYear.Name,
                    NetResult = netResult,
                    RetainedEarningsAccountId = retainedAccount.Id
                });
        }

        db.FiscalYearClosings.Add(new FiscalYearClosing
        {
            FiscalYearId = fiscalYear.Id,
            CompanyId = companyId,
            ClosingJournalEntryId = closingJournalId,
            RetainedEarningsAccountId = retainedAccount.Id,
            CreatedByUserId = userId,
            NetResult = netResult
        });

        var periods = await db.FiscalPeriods
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYear.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var period in periods)
        {
            period.IsClosed = true;
        }

        fiscalYear.IsClosed = true;

        AddAuditLog(
            companyId,
            userId,
            "FiscalYear",
            fiscalYear.Id,
            "FISCAL_YEAR_FINALIZE",
            null,
            new
            {
                fiscalYear.Name,
                NetResult = netResult,
                ClosingJournalEntryId = closingJournalId,
                ClosingJournalNumber = closingJournalNumber,
                RetainedEarningsAccountId = retainedAccount.Id
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new FinalizeFiscalYearResponse(
            fiscalYear.Id,
            closingJournalId,
            closingJournalNumber,
            netResult,
            false);
    }

    public async Task<ReopenFinalizedFiscalYearResponse> ReopenFinalizedFiscalYearAsync(
        Guid companyId,
        Guid userId,
        Guid fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var fiscalYear = await db.FiscalYears
            .FirstOrDefaultAsync(
                x =>
                    x.Id == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Fiscal year does not exist in this company.");

        var closing = await db.FiscalYearClosings
            .FirstOrDefaultAsync(
                x =>
                    x.FiscalYearId == fiscalYearId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Fiscal year has no active finalization to reopen.");

        Guid? reversalJournalId = null;
        string? reversalNumber = null;

        if (closing.ClosingJournalEntryId is Guid closingJournalId)
        {
            var original = await db.JournalEntries
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == closingJournalId &&
                        x.CompanyId == companyId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Fiscal-year closing journal is missing.");

            var alreadyReversed = await db.JournalReversalLinks.AnyAsync(
                x => x.OriginalJournalEntryId == original.Id,
                cancellationToken);

            if (alreadyReversed)
            {
                throw new InvalidOperationException(
                    "Fiscal-year closing journal already has a reversal.");
            }

            var reversalLines = original.Lines
                .Select(line => new CreateJournalLineRequest(
                    line.AccountId,
                    "بازگشایی اختتام سال: " +
                        (line.Description ?? original.Description ?? string.Empty),
                    line.Credit,
                    line.Debit))
                .ToArray();

            var reversal = await CreatePostedJournalCoreAsync(
                companyId,
                userId,
                new CreateJournalRequest(
                    null,
                    fiscalYear.EndDate,
                    $"بازگشایی سند اختتام {fiscalYear.Name}",
                    reversalLines,
                    fiscalYear.Id),
                cancellationToken,
                enforceFiscalControls: false);

            reversalJournalId = reversal.Id;
            reversalNumber = reversal.Number;

            db.JournalReversalLinks.Add(new JournalReversalLink
            {
                OriginalJournalEntryId = original.Id,
                ReversalJournalEntryId = reversal.Id,
                CreatedByUserId = userId,
                Reason = $"بازگشایی بستن نهایی {fiscalYear.Name}"
            });

            AddAuditLog(
                companyId,
                userId,
                "JournalEntry",
                original.Id,
                "YEAR_END_CLOSING_REVERSE",
                $"بازگشایی {fiscalYear.Name}",
                new
                {
                    ReversalJournalEntryId = reversal.Id,
                    ReversalNumber = reversal.Number
                });

            AddAuditLog(
                companyId,
                userId,
                "JournalEntry",
                reversal.Id,
                "YEAR_END_REOPEN_POST",
                null,
                new
                {
                    OriginalClosingJournalEntryId = original.Id,
                    original.Number
                });
        }

        db.FiscalYearClosings.Remove(closing);
        fiscalYear.IsClosed = false;

        var lastPeriod = await db.FiscalPeriods
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYear.Id)
            .OrderByDescending(x => x.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastPeriod is not null)
        {
            lastPeriod.IsClosed = false;
        }

        AddAuditLog(
            companyId,
            userId,
            "FiscalYear",
            fiscalYear.Id,
            "FISCAL_YEAR_FINALIZATION_REOPEN",
            null,
            new
            {
                fiscalYear.Name,
                ReversalJournalEntryId = reversalJournalId,
                ReversalJournalNumber = reversalNumber
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ReopenFinalizedFiscalYearResponse(
            fiscalYear.Id,
            reversalJournalId,
            reversalNumber);
    }

    public async Task<FiscalYear> EnsureDefaultFiscalYearAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var existing = await db.FiscalYears
            .Where(x =>
                x.CompanyId == companyId &&
                x.StartDate <= today &&
                x.EndDate >= today)
            .OrderByDescending(x => x.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (IsStandardPersianFiscalYear(existing))
            {
                await EnsureStandardFiscalPeriodsCoreAsync(
                    companyId,
                    existing,
                    cancellationToken);
            }

            return existing;
        }

        var persianCalendar = new PersianCalendar();
        var now = DateTime.Today;
        var persianYear = persianCalendar.GetYear(now);

        var start = DateOnly.FromDateTime(
            persianCalendar.ToDateTime(
                persianYear,
                1,
                1,
                0,
                0,
                0,
                0));

        var nextStart = DateOnly.FromDateTime(
            persianCalendar.ToDateTime(
                persianYear + 1,
                1,
                1,
                0,
                0,
                0,
                0));

        var end = nextStart.AddDays(-1);
        var hasDefault = await db.FiscalYears.AnyAsync(
            x => x.CompanyId == companyId && x.IsDefault,
            cancellationToken);

        return await CreateFiscalYearAsync(
            companyId,
            new CreateFiscalYearRequest(
                $"سال مالی {persianYear}",
                persianYear,
                start,
                end,
                !hasDefault),
            cancellationToken);
    }

    public async Task<IReadOnlyList<DetailAccountView>> GetDetailAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var details = await db.DetailAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var states = await db.DetailAccountSyncStates
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(
                x => x.DetailAccountId,
                cancellationToken);

        return details
            .Select(detail =>
            {
                states.TryGetValue(detail.Id, out var state);

                return ToDetailAccountView(
                    detail,
                    state?.Revision ?? 1,
                    state?.UpdatedAt ?? detail.CreatedAt);
            })
            .ToArray();
    }

    public async Task<DetailAccountView> CreateDetailAccountAsync(
        Guid companyId,
        CreateDetailAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();
        var nationalId = request.NationalId?.Trim();

        ValidateDetailAccountFields(code, name);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var duplicateCode = await db.DetailAccounts.AnyAsync(
            x => x.CompanyId == companyId && x.Code == code,
            cancellationToken);

        if (duplicateCode)
        {
            throw new InvalidOperationException(
                "Detail account code already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var detail = new DetailAccount
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = request.Type,
            NationalId = string.IsNullOrWhiteSpace(nationalId)
                ? null
                : nationalId
        };

        db.DetailAccounts.Add(detail);
        db.DetailAccountSyncStates.Add(new DetailAccountSyncState
        {
            DetailAccountId = detail.Id,
            CompanyId = companyId,
            Revision = 1,
            UpdatedAt = now
        });
        db.DetailAccountServerChanges.Add(new DetailAccountServerChange
        {
            CompanyId = companyId,
            DetailAccountId = detail.Id,
            Revision = 1,
            Operation = "Upsert",
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDetailAccountView(detail, 1, now);
    }

    public Task<SyncDetailAccountResponse> SyncDetailAccountAsync(
        Guid companyId,
        SyncDetailAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        return SyncDetailAccountAsync(
            companyId,
            null,
            request,
            cancellationToken);
    }

    public async Task<SyncDetailAccountResponse> SyncDetailAccountAsync(
        Guid companyId,
        Guid? userId,
        SyncDetailAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var changeId = request.ChangeId.Trim();
        var code = request.Code.Trim();
        var name = request.Name.Trim();
        var nationalId = request.NationalId?.Trim();

        if (string.IsNullOrWhiteSpace(changeId))
        {
            throw new ArgumentException("ChangeId is required.");
        }

        if (request.BaseRevision < 0)
        {
            throw new ArgumentException("BaseRevision cannot be negative.");
        }

        ValidateDetailAccountFields(code, name);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var receipt = await db.DetailAccountSyncReceipts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.ChangeId == changeId,
                cancellationToken);

        if (receipt is not null)
        {
            if (receipt.DetailAccountId != request.EntityId)
            {
                throw new InvalidOperationException(
                    "ChangeId is already associated with another detail account.");
            }

            var duplicateDetail = await db.DetailAccounts
                .AsNoTracking()
                .FirstAsync(
                    x =>
                        x.Id == receipt.DetailAccountId &&
                        x.CompanyId == companyId,
                    cancellationToken);

            var duplicateState = await db.DetailAccountSyncStates
                .AsNoTracking()
                .FirstAsync(
                    x => x.DetailAccountId == duplicateDetail.Id,
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new SyncDetailAccountResponse(
                "Applied",
                ToDetailAccountView(
                    duplicateDetail,
                    duplicateState.Revision,
                    duplicateState.UpdatedAt),
                null,
                true);
        }

        var detail = await db.DetailAccounts
            .FirstOrDefaultAsync(
                x =>
                    x.Id == request.EntityId &&
                    x.CompanyId == companyId,
                cancellationToken);

        if (detail is null)
        {
            if (request.BaseRevision != 0)
            {
                await transaction.CommitAsync(cancellationToken);

                return new SyncDetailAccountResponse(
                    "Conflict",
                    null,
                    new DetailAccountSyncConflict(
                        null,
                        request.BaseRevision),
                    false);
            }

            var duplicateCode = await db.DetailAccounts.AnyAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.Code == code,
                cancellationToken);

            if (duplicateCode)
            {
                throw new InvalidOperationException(
                    "Detail account code already exists.");
            }

            var now = DateTimeOffset.UtcNow;
            detail = new DetailAccount
            {
                Id = request.EntityId,
                CompanyId = companyId,
                Code = code,
                Name = name,
                Type = request.Type,
                NationalId = string.IsNullOrWhiteSpace(nationalId)
                    ? null
                    : nationalId,
                IsActive = request.IsActive
            };

            db.DetailAccounts.Add(detail);
            db.DetailAccountSyncStates.Add(new DetailAccountSyncState
            {
                DetailAccountId = detail.Id,
                CompanyId = companyId,
                Revision = 1,
                UpdatedAt = now
            });
            db.DetailAccountServerChanges.Add(new DetailAccountServerChange
            {
                CompanyId = companyId,
                DetailAccountId = detail.Id,
                Revision = 1,
                Operation = "Upsert",
                CreatedAt = now
            });
            db.DetailAccountSyncReceipts.Add(new DetailAccountSyncReceipt
            {
                CompanyId = companyId,
                ChangeId = changeId,
                DetailAccountId = detail.Id,
                AppliedRevision = 1,
                CreatedAt = now
            });

            if (userId is Guid actorId)
            {
                AddAuditLog(
                    companyId,
                    actorId,
                    "DetailAccount",
                    detail.Id,
                    "DETAIL_CREATE_SYNC",
                    null,
                    new
                    {
                        detail.Code,
                        detail.Name,
                        Type = detail.Type.ToString(),
                        Revision = 1
                    });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new SyncDetailAccountResponse(
                "Applied",
                ToDetailAccountView(detail, 1, now),
                null,
                false);
        }

        var state = await db.DetailAccountSyncStates
            .FirstOrDefaultAsync(
                x => x.DetailAccountId == detail.Id,
                cancellationToken);

        if (state is null)
        {
            state = new DetailAccountSyncState
            {
                DetailAccountId = detail.Id,
                CompanyId = companyId,
                Revision = 1,
                UpdatedAt = detail.CreatedAt
            };

            db.DetailAccountSyncStates.Add(state);
        }

        if (state.Revision != request.BaseRevision)
        {
            await transaction.CommitAsync(cancellationToken);

            return new SyncDetailAccountResponse(
                "Conflict",
                null,
                new DetailAccountSyncConflict(
                    ToDetailAccountView(
                        detail,
                        state.Revision,
                        state.UpdatedAt),
                    request.BaseRevision),
                false);
        }

        var codeUsedByAnother = await db.DetailAccounts.AnyAsync(
            x =>
                x.CompanyId == companyId &&
                x.Id != detail.Id &&
                x.Code == code,
            cancellationToken);

        if (codeUsedByAnother)
        {
            throw new InvalidOperationException(
                "Detail account code already exists.");
        }

        detail.Code = code;
        detail.Name = name;
        detail.Type = request.Type;
        detail.NationalId = string.IsNullOrWhiteSpace(nationalId)
            ? null
            : nationalId;
        detail.IsActive = request.IsActive;

        state.Revision++;
        state.UpdatedAt = DateTimeOffset.UtcNow;

        db.DetailAccountServerChanges.Add(new DetailAccountServerChange
        {
            CompanyId = companyId,
            DetailAccountId = detail.Id,
            Revision = state.Revision,
            Operation = "Upsert",
            CreatedAt = state.UpdatedAt
        });
        db.DetailAccountSyncReceipts.Add(new DetailAccountSyncReceipt
        {
            CompanyId = companyId,
            ChangeId = changeId,
            DetailAccountId = detail.Id,
            AppliedRevision = state.Revision,
            CreatedAt = state.UpdatedAt
        });

        if (userId is Guid actorId)
        {
            AddAuditLog(
                companyId,
                actorId,
                "DetailAccount",
                detail.Id,
                "DETAIL_UPDATE_SYNC",
                null,
                new
                {
                    detail.Code,
                    detail.Name,
                    Type = detail.Type.ToString(),
                    Revision = state.Revision
                });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SyncDetailAccountResponse(
            "Applied",
            ToDetailAccountView(
                detail,
                state.Revision,
                state.UpdatedAt),
            null,
            false);
    }

    public async Task<DetailAccountPullResponse> PullDetailAccountChangesAsync(
        Guid companyId,
        long afterCursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (afterCursor < 0)
        {
            throw new ArgumentException("Cursor cannot be negative.");
        }

        var pageSize = Math.Clamp(limit, 1, 200);

        var changeRows = await db.DetailAccountServerChanges
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Sequence > afterCursor)
            .OrderBy(x => x.Sequence)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);

        var hasMore = changeRows.Length > pageSize;
        var page = changeRows.Take(pageSize).ToArray();

        if (page.Length == 0)
        {
            return new DetailAccountPullResponse(
                afterCursor,
                false,
                []);
        }

        var ids = page
            .Select(x => x.DetailAccountId)
            .Distinct()
            .ToArray();

        var details = await db.DetailAccounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var states = await db.DetailAccountSyncStates
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                ids.Contains(x.DetailAccountId))
            .ToDictionaryAsync(
                x => x.DetailAccountId,
                cancellationToken);

        var changes = new List<DetailAccountServerChangeView>();

        foreach (var change in page)
        {
            if (!details.TryGetValue(change.DetailAccountId, out var detail))
            {
                continue;
            }

            states.TryGetValue(detail.Id, out var state);

            changes.Add(new DetailAccountServerChangeView(
                change.Sequence,
                ToDetailAccountView(
                    detail,
                    state?.Revision ?? change.Revision,
                    state?.UpdatedAt ?? change.CreatedAt)));
        }

        return new DetailAccountPullResponse(
            page[^1].Sequence,
            hasMore,
            changes);
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
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(from, to);

        var query = db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted);

        if (from is DateOnly fromDate)
        {
            query = query.Where(x => x.DocumentDate >= fromDate);
        }

        if (to is DateOnly toDate)
        {
            query = query.Where(x => x.DocumentDate <= toDate);
        }

        return await query
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
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var entry = await CreatePostedJournalCoreAsync(
            companyId,
            userId,
            request,
            cancellationToken);

        AddAuditLog(
            companyId,
            userId,
            "JournalEntry",
            entry.Id,
            "POST",
            null,
            new { entry.Number, entry.DocumentDate });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entry;
    }

    public async Task<SyncJournalResponse> SyncJournalAsync(
        Guid companyId,
        Guid userId,
        SyncJournalRequest request,
        CancellationToken cancellationToken = default)
    {
        var changeId = request.ChangeId.Trim();
        var localDocumentId = request.LocalDocumentId.Trim();

        if (string.IsNullOrWhiteSpace(changeId) ||
            string.IsNullOrWhiteSpace(localDocumentId))
        {
            throw new ArgumentException(
                "ChangeId and LocalDocumentId are required.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var existingReceipt = await db.JournalSyncReceipts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.CompanyId == companyId &&
                    (x.ChangeId == changeId ||
                     x.LocalDocumentId == localDocumentId),
                cancellationToken);

        if (existingReceipt is not null)
        {
            if (existingReceipt.ChangeId == changeId &&
                existingReceipt.LocalDocumentId != localDocumentId)
            {
                throw new InvalidOperationException(
                    "ChangeId is already associated with a different local document.");
            }

            var existingJournal = await db.JournalEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == existingReceipt.JournalEntryId &&
                        x.CompanyId == companyId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Sync receipt references a missing journal.");

            await transaction.CommitAsync(cancellationToken);

            return new SyncJournalResponse(
                existingJournal.Id,
                existingJournal.Number,
                existingJournal.Status,
                existingJournal.PostedAt,
                true);
        }

        var journalRequest = new CreateJournalRequest(
            Number: null,
            DocumentDate: request.DocumentDate,
            Description: request.Description,
            Lines: request.Lines,
            FiscalYearId: request.FiscalYearId);

        var entry = await CreatePostedJournalCoreAsync(
            companyId,
            userId,
            journalRequest,
            cancellationToken);

        db.JournalSyncReceipts.Add(new JournalSyncReceipt
        {
            CompanyId = companyId,
            ChangeId = changeId,
            LocalDocumentId = localDocumentId,
            JournalEntryId = entry.Id
        });

        AddAuditLog(
            companyId,
            userId,
            "JournalEntry",
            entry.Id,
            "POST_SYNC",
            null,
            new { entry.Number, entry.DocumentDate, LocalDocumentId = localDocumentId });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SyncJournalResponse(
            entry.Id,
            entry.Number,
            entry.Status,
            entry.PostedAt,
            false);
    }

    public async Task<ReverseJournalResponse> ReverseJournalAsync(
        Guid companyId,
        Guid userId,
        Guid journalEntryId,
        ReverseJournalRequest request,
        CancellationToken cancellationToken = default)
    {
        var reason = request.Reason.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Reversal reason is required.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var original = await db.JournalEntries
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == journalEntryId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Journal entry does not exist in this company.");

        if (original.Status != JournalStatus.Posted)
        {
            throw new InvalidOperationException(
                "Only posted journal entries can be reversed.");
        }

        var isFiscalYearClosing = await db.FiscalYearClosings.AnyAsync(
            x => x.ClosingJournalEntryId == original.Id,
            cancellationToken);

        if (isFiscalYearClosing)
        {
            throw new InvalidOperationException(
                "Fiscal-year closing journals must be reversed through the fiscal-year reopen workflow.");
        }

        var alreadyReversed = await db.JournalReversalLinks.AnyAsync(
            x => x.OriginalJournalEntryId == original.Id,
            cancellationToken);

        if (alreadyReversed)
        {
            throw new InvalidOperationException(
                "Journal entry already has a reversal.");
        }

        var isReversalEntry = await db.JournalReversalLinks.AnyAsync(
            x => x.ReversalJournalEntryId == original.Id,
            cancellationToken);

        if (isReversalEntry)
        {
            throw new InvalidOperationException(
                "A reversal journal cannot be reversed again.");
        }

        var lineIds = original.Lines
            .Select(x => x.Id)
            .ToArray();

        var dimensions = lineIds.Length == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.JournalLineDimensions
                .AsNoTracking()
                .Where(x => lineIds.Contains(x.JournalLineId))
                .ToDictionaryAsync(
                    x => x.JournalLineId,
                    x => x.DetailAccountId,
                    cancellationToken);

        var reversalLines = original.Lines
            .Select(line =>
            {
                dimensions.TryGetValue(
                    line.Id,
                    out var detailAccountId);

                return new CreateJournalLineRequest(
                    line.AccountId,
                    "برگشت: " + (line.Description ?? original.Description ?? string.Empty),
                    line.Credit,
                    line.Debit,
                    detailAccountId);
            })
            .ToArray();

        var reversal = await CreatePostedJournalCoreAsync(
            companyId,
            userId,
            new CreateJournalRequest(
                null,
                request.DocumentDate,
                "برگشت سند " + original.Number + " — " + reason,
                reversalLines,
                request.FiscalYearId),
            cancellationToken);

        var now = DateTimeOffset.UtcNow;

        db.JournalReversalLinks.Add(new JournalReversalLink
        {
            OriginalJournalEntryId = original.Id,
            ReversalJournalEntryId = reversal.Id,
            CreatedByUserId = userId,
            Reason = reason,
            CreatedAt = now
        });

        AddAuditLog(
            companyId,
            userId,
            "JournalEntry",
            original.Id,
            "REVERSE",
            reason,
            new
            {
                OriginalNumber = original.Number,
                ReversalJournalEntryId = reversal.Id,
                ReversalNumber = reversal.Number
            });

        AddAuditLog(
            companyId,
            userId,
            "JournalEntry",
            reversal.Id,
            "REVERSAL_POST",
            reason,
            new
            {
                OriginalJournalEntryId = original.Id,
                OriginalNumber = original.Number,
                ReversalNumber = reversal.Number
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ReverseJournalResponse(
            original.Id,
            reversal.Id,
            reversal.Number,
            reversal.PostedAt);
    }

    public async Task<IReadOnlyList<AccountingAuditView>> GetAuditLogsAsync(
        Guid companyId,
        Guid? entityId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 500);

        var query = db.AccountingAuditLogs
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (entityId is Guid id)
        {
            query = query.Where(x => x.EntityId == id);
        }

        var logs = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        var userIds = logs
            .Select(x => x.UserId)
            .Distinct()
            .ToArray();

        var userNames = userIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Users
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    userIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    x => x.DisplayName,
                    cancellationToken);

        return logs
            .Select(log => new AccountingAuditView(
                log.Id,
                log.EntityType,
                log.EntityId,
                log.Action,
                log.Reason,
                log.UserId,
                userNames.GetValueOrDefault(
                    log.UserId,
                    "کاربر نامشخص"),
                log.CreatedAt))
            .ToArray();
    }

    private void AddAuditLog(
        Guid companyId,
        Guid userId,
        string entityType,
        Guid entityId,
        string action,
        string? reason,
        object? payload)
    {
        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Reason = reason,
            PayloadJson = payload is null
                ? null
                : JsonSerializer.Serialize(payload)
        });
    }

    private async Task<JournalEntry> CreatePostedJournalCoreAsync(
        Guid companyId,
        Guid userId,
        CreateJournalRequest request,
        CancellationToken cancellationToken,
        bool enforceFiscalControls = true)
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

        var fiscalYear = await ResolveFiscalYearAsync(
            companyId,
            request.FiscalYearId,
            request.DocumentDate,
            cancellationToken);

        if (enforceFiscalControls)
        {
            if (fiscalYear.IsClosed)
            {
                throw new InvalidOperationException(
                    "The selected fiscal year is closed.");
            }

            await EnsurePostingPeriodIsOpenAsync(
                companyId,
                fiscalYear.Id,
                request.DocumentDate,
                cancellationToken);
        }

        var detailIds = request.Lines
            .Where(x => x.DetailAccountId.HasValue)
            .Select(x => x.DetailAccountId!.Value)
            .Distinct()
            .ToArray();

        if (detailIds.Length > 0)
        {
            var validDetailCount = await db.DetailAccounts.CountAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.IsActive &&
                    detailIds.Contains(x.Id),
                cancellationToken);

            if (validDetailCount != detailIds.Length)
            {
                throw new ArgumentException(
                    "One or more floating detail accounts are unavailable.");
            }
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

        var containsControlAccount = await db.Accounts.AnyAsync(
            x =>
                x.CompanyId == companyId &&
                x.ParentId.HasValue &&
                accountIds.Contains(x.ParentId.Value),
            cancellationToken);

        if (containsControlAccount)
        {
            throw new ArgumentException(
                "Journal lines can only use postable leaf accounts.");
        }

        var number = request.Number?.Trim();

        if (string.IsNullOrWhiteSpace(number))
        {
            number = await GenerateJournalNumberAsync(
                companyId,
                fiscalYear,
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
        db.JournalEntryFiscalYears.Add(new JournalEntryFiscalYear
        {
            JournalEntryId = entry.Id,
            FiscalYearId = fiscalYear.Id
        });

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var requestLine = request.Lines[index];

            if (requestLine.DetailAccountId is not Guid detailAccountId)
            {
                continue;
            }

            db.JournalLineDimensions.Add(new JournalLineDimension
            {
                JournalLineId = entry.Lines[index].Id,
                DetailAccountId = detailAccountId
            });
        }

        db.JournalServerChanges.Add(new JournalServerChange
        {
            CompanyId = companyId,
            JournalEntryId = entry.Id
        });

        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<ServerJournalPullResponse> PullJournalChangesAsync(
        Guid companyId,
        long afterCursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (afterCursor < 0)
        {
            throw new ArgumentException("Cursor cannot be negative.");
        }

        var pageSize = Math.Clamp(limit, 1, 200);

        var changeRows = await db.JournalServerChanges
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Sequence > afterCursor)
            .OrderBy(x => x.Sequence)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);

        var hasMore = changeRows.Length > pageSize;
        var page = changeRows.Take(pageSize).ToArray();

        if (page.Length == 0)
        {
            return new ServerJournalPullResponse(
                afterCursor,
                false,
                []);
        }

        var journalIds = page
            .Select(x => x.JournalEntryId)
            .ToArray();

        var journals = await db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                journalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var fiscalLinks = await db.JournalEntryFiscalYears
            .AsNoTracking()
            .Where(x => journalIds.Contains(x.JournalEntryId))
            .ToDictionaryAsync(
                x => x.JournalEntryId,
                x => x.FiscalYearId,
                cancellationToken);

        var lineIds = journals.Values
            .SelectMany(x => x.Lines)
            .Select(x => x.Id)
            .ToArray();

        var dimensions = lineIds.Length == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.JournalLineDimensions
                .AsNoTracking()
                .Where(x => lineIds.Contains(x.JournalLineId))
                .ToDictionaryAsync(
                    x => x.JournalLineId,
                    x => x.DetailAccountId,
                    cancellationToken);

        var reversalOf = await db.JournalReversalLinks
            .AsNoTracking()
            .Where(x => journalIds.Contains(x.ReversalJournalEntryId))
            .ToDictionaryAsync(
                x => x.ReversalJournalEntryId,
                x => x.OriginalJournalEntryId,
                cancellationToken);

        var changes = new List<ServerJournalChangeView>(page.Length);

        foreach (var change in page)
        {
            if (!journals.TryGetValue(change.JournalEntryId, out var journal))
            {
                continue;
            }

            fiscalLinks.TryGetValue(journal.Id, out var fiscalYearId);

            var lines = journal.Lines
                .Select(line =>
                {
                    dimensions.TryGetValue(line.Id, out var detailAccountId);

                    return new ServerJournalLineView(
                        line.AccountId,
                        detailAccountId,
                        line.Description,
                        line.Debit,
                        line.Credit);
                })
                .ToArray();

            reversalOf.TryGetValue(
                journal.Id,
                out var originalJournalEntryId);

            changes.Add(new ServerJournalChangeView(
                change.Sequence,
                journal.Id,
                journal.Number,
                fiscalYearId == Guid.Empty ? null : fiscalYearId,
                journal.DocumentDate,
                journal.Description,
                journal.Status,
                journal.PostedAt,
                originalJournalEntryId == Guid.Empty
                    ? null
                    : originalJournalEntryId,
                lines));
        }

        var nextCursor = page[^1].Sequence;

        return new ServerJournalPullResponse(
            nextCursor,
            hasMore,
            changes);
    }

    public async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
        Guid companyId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(from, to);

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var lookup = accounts.ToDictionary(x => x.Id);
        var parentIds = accounts
            .Where(x => x.ParentId.HasValue)
            .Select(x => x.ParentId!.Value)
            .ToHashSet();

        var journalQuery = db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted);

        if (to is DateOnly toDate)
        {
            journalQuery = journalQuery.Where(
                x => x.DocumentDate <= toDate);
        }

        var journals = await journalQuery
            .ToArrayAsync(cancellationToken);

        var opening = accounts.ToDictionary(
            x => x.Id,
            _ => 0m);
        var debit = accounts.ToDictionary(
            x => x.Id,
            _ => 0m);
        var credit = accounts.ToDictionary(
            x => x.Id,
            _ => 0m);

        foreach (var journal in journals)
        {
            var isOpening =
                from is DateOnly fromDate &&
                journal.DocumentDate < fromDate;

            foreach (var line in journal.Lines)
            {
                if (!lookup.ContainsKey(line.AccountId))
                {
                    continue;
                }

                var currentId = line.AccountId;
                var visited = new HashSet<Guid>();

                while (visited.Add(currentId) &&
                       lookup.TryGetValue(
                           currentId,
                           out var currentAccount))
                {
                    if (isOpening)
                    {
                        opening[currentId] +=
                            line.Debit - line.Credit;
                    }
                    else
                    {
                        debit[currentId] += line.Debit;
                        credit[currentId] += line.Credit;
                    }

                    if (currentAccount.ParentId is not Guid parentId)
                    {
                        break;
                    }

                    currentId = parentId;
                }
            }
        }

        return accounts
            .Select(account =>
            {
                var level = ResolveLevel(account, lookup);
                var openingBalance = opening[account.Id];
                var debitTurnover = debit[account.Id];
                var creditTurnover = credit[account.Id];

                return new TrialBalanceRow(
                    account.Id,
                    account.Code,
                    account.Name,
                    GetLevelTitle(level),
                    !parentIds.Contains(account.Id),
                    openingBalance,
                    debitTurnover,
                    creditTurnover,
                    openingBalance +
                        debitTurnover -
                        creditTurnover);
            })
            .ToArray();
    }

    public async Task<GeneralLedgerReportResponse> GetGeneralLedgerAsync(
        Guid companyId,
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(from, to);

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var accountLookup = accounts.ToDictionary(x => x.Id);

        HashSet<Guid>? targetAccountIds = null;

        if (accountId is Guid selectedAccountId)
        {
            if (!accountLookup.ContainsKey(selectedAccountId))
            {
                throw new ArgumentException(
                    "Selected account does not exist in this company.");
            }

            targetAccountIds = GetDescendantAccountIds(
                selectedAccountId,
                accounts);
        }

        var journalQuery = db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted);

        if (to is DateOnly toDate)
        {
            journalQuery = journalQuery.Where(
                x => x.DocumentDate <= toDate);
        }

        var ordered = await journalQuery
            .OrderBy(x => x.DocumentDate)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

        decimal debitTurnover = 0;
        decimal creditTurnover = 0;
        var rows = new List<GeneralLedgerRow>();

        if (targetAccountIds is not null)
        {
            decimal openingBalance = 0;

            if (from is DateOnly fromDate)
            {
                openingBalance = ordered
                    .Where(x => x.DocumentDate < fromDate)
                    .SelectMany(x => x.Lines)
                    .Where(x =>
                        targetAccountIds.Contains(x.AccountId))
                    .Sum(x => x.Debit - x.Credit);
            }

            var running = openingBalance;

            foreach (var journal in ordered)
            {
                if (from is DateOnly startDate &&
                    journal.DocumentDate < startDate)
                {
                    continue;
                }

                foreach (var line in journal.Lines)
                {
                    if (!targetAccountIds.Contains(line.AccountId) ||
                        !accountLookup.TryGetValue(
                            line.AccountId,
                            out var actualAccount))
                    {
                        continue;
                    }

                    debitTurnover += line.Debit;
                    creditTurnover += line.Credit;
                    running += line.Debit - line.Credit;

                    rows.Add(new GeneralLedgerRow(
                        journal.Id,
                        journal.Number,
                        journal.DocumentDate,
                        actualAccount.Id,
                        actualAccount.Code,
                        actualAccount.Name,
                        line.Description ?? journal.Description,
                        line.Debit,
                        line.Credit,
                        running));
                }
            }

            return new GeneralLedgerReportResponse(
                accountId,
                openingBalance,
                debitTurnover,
                creditTurnover,
                running,
                rows);
        }

        var balances = accounts.ToDictionary(
            x => x.Id,
            _ => 0m);

        if (from is DateOnly allFromDate)
        {
            foreach (var journal in ordered.Where(
                         x => x.DocumentDate < allFromDate))
            {
                foreach (var line in journal.Lines)
                {
                    if (balances.ContainsKey(line.AccountId))
                    {
                        balances[line.AccountId] +=
                            line.Debit - line.Credit;
                    }
                }
            }
        }

        var openingTotal = balances.Values.Sum();

        foreach (var journal in ordered)
        {
            if (from is DateOnly startDate &&
                journal.DocumentDate < startDate)
            {
                continue;
            }

            foreach (var line in journal.Lines)
            {
                if (!accountLookup.TryGetValue(
                        line.AccountId,
                        out var account))
                {
                    continue;
                }

                balances[account.Id] +=
                    line.Debit - line.Credit;
                debitTurnover += line.Debit;
                creditTurnover += line.Credit;

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
                    balances[account.Id]));
            }
        }

        return new GeneralLedgerReportResponse(
            null,
            openingTotal,
            debitTurnover,
            creditTurnover,
            balances.Values.Sum(),
            rows);
    }

    public async Task<DetailLedgerReportResponse> GetDetailLedgerAsync(
        Guid companyId,
        Guid detailAccountId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(from, to);

        var detail = await db.DetailAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.Id == detailAccountId &&
                    x.CompanyId == companyId,
                cancellationToken)
            ?? throw new ArgumentException(
                "Detail account does not exist in this company.");

        var accountLookup = await db.Accounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var journalQuery = db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted);

        if (to is DateOnly toDate)
        {
            journalQuery = journalQuery.Where(
                x => x.DocumentDate <= toDate);
        }

        var journals = await journalQuery
            .OrderBy(x => x.DocumentDate)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var lineIds = journals
            .SelectMany(x => x.Lines)
            .Select(x => x.Id)
            .ToArray();

        var matchingLineIds = lineIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.JournalLineDimensions
                .AsNoTracking()
                .Where(x =>
                    lineIds.Contains(x.JournalLineId) &&
                    x.DetailAccountId == detailAccountId)
                .Select(x => x.JournalLineId)
                .ToArrayAsync(cancellationToken))
                .ToHashSet();

        decimal openingBalance = 0;
        decimal runningBalance = 0;
        decimal debitTurnover = 0;
        decimal creditTurnover = 0;
        var rows = new List<DetailLedgerRow>();

        foreach (var journal in journals)
        {
            foreach (var line in journal.Lines)
            {
                if (!matchingLineIds.Contains(line.Id))
                {
                    continue;
                }

                var movement = line.Debit - line.Credit;

                if (from is DateOnly fromDate &&
                    journal.DocumentDate < fromDate)
                {
                    openingBalance += movement;
                    continue;
                }

                runningBalance =
                    openingBalance +
                    debitTurnover -
                    creditTurnover +
                    movement;

                debitTurnover += line.Debit;
                creditTurnover += line.Credit;

                if (!accountLookup.TryGetValue(
                        line.AccountId,
                        out var account))
                {
                    continue;
                }

                rows.Add(new DetailLedgerRow(
                    journal.Id,
                    journal.Number,
                    journal.DocumentDate,
                    account.Id,
                    account.Code,
                    account.Name,
                    detail.Id,
                    detail.Code,
                    detail.Name,
                    line.Description ?? journal.Description,
                    line.Debit,
                    line.Credit,
                    runningBalance));
            }
        }

        var closingBalance =
            openingBalance + debitTurnover - creditTurnover;

        return new DetailLedgerReportResponse(
            detail.Id,
            openingBalance,
            debitTurnover,
            creditTurnover,
            closingBalance,
            rows);
    }

    public async Task<ProfitLossReportResponse> GetProfitLossAsync(
        Guid companyId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(from, to);

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.Type == AccountType.Revenue ||
                 x.Type == AccountType.Expense))
            .OrderBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var activeClosingJournalIds = await db.FiscalYearClosings
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ClosingJournalEntryId.HasValue)
            .Select(x => x.ClosingJournalEntryId!.Value)
            .ToArrayAsync(cancellationToken);

        var journalQuery = db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted &&
                !activeClosingJournalIds.Contains(x.Id));

        if (from is DateOnly fromDate)
        {
            journalQuery = journalQuery.Where(
                x => x.DocumentDate >= fromDate);
        }

        if (to is DateOnly toDate)
        {
            journalQuery = journalQuery.Where(
                x => x.DocumentDate <= toDate);
        }

        var journals = await journalQuery
            .ToArrayAsync(cancellationToken);

        var rows = new List<ProfitLossRow>();
        decimal revenueTotal = 0;
        decimal expenseTotal = 0;

        foreach (var account in accounts)
        {
            var lines = journals
                .SelectMany(x => x.Lines)
                .Where(x => x.AccountId == account.Id)
                .ToArray();

            decimal amount;

            if (account.Type == AccountType.Revenue)
            {
                amount = lines.Sum(x => x.Credit - x.Debit);
                revenueTotal += amount;
            }
            else
            {
                amount = lines.Sum(x => x.Debit - x.Credit);
                expenseTotal += amount;
            }

            if (amount == 0)
            {
                continue;
            }

            rows.Add(new ProfitLossRow(
                account.Id,
                account.Code,
                account.Name,
                account.Type == AccountType.Revenue
                    ? "Revenue"
                    : "Expense",
                amount));
        }

        return new ProfitLossReportResponse(
            from,
            to,
            revenueTotal,
            expenseTotal,
            revenueTotal - expenseTotal,
            rows);
    }

    public async Task<BalanceSheetReportResponse> GetBalanceSheetAsync(
        Guid companyId,
        DateOnly asOf,
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
                x.Status == JournalStatus.Posted &&
                x.DocumentDate <= asOf)
            .ToArrayAsync(cancellationToken);

        var rows = new List<BalanceSheetRow>();
        decimal assetTotal = 0;
        decimal liabilityTotal = 0;
        decimal equityTotal = 0;
        decimal revenueResult = 0;
        decimal expenseResult = 0;

        foreach (var account in accounts)
        {
            var lines = journals
                .SelectMany(x => x.Lines)
                .Where(x => x.AccountId == account.Id)
                .ToArray();

            if (lines.Length == 0)
            {
                continue;
            }

            switch (account.Type)
            {
                case AccountType.Asset:
                {
                    var amount = lines.Sum(
                        x => x.Debit - x.Credit);

                    if (amount != 0)
                    {
                        assetTotal += amount;
                        rows.Add(new BalanceSheetRow(
                            account.Id,
                            account.Code,
                            account.Name,
                            "Asset",
                            amount));
                    }

                    break;
                }

                case AccountType.Liability:
                {
                    var amount = lines.Sum(
                        x => x.Credit - x.Debit);

                    if (amount != 0)
                    {
                        liabilityTotal += amount;
                        rows.Add(new BalanceSheetRow(
                            account.Id,
                            account.Code,
                            account.Name,
                            "Liability",
                            amount));
                    }

                    break;
                }

                case AccountType.Equity:
                {
                    var amount = lines.Sum(
                        x => x.Credit - x.Debit);

                    if (amount != 0)
                    {
                        equityTotal += amount;
                        rows.Add(new BalanceSheetRow(
                            account.Id,
                            account.Code,
                            account.Name,
                            "Equity",
                            amount));
                    }

                    break;
                }

                case AccountType.Revenue:
                    revenueResult += lines.Sum(
                        x => x.Credit - x.Debit);
                    break;

                case AccountType.Expense:
                    expenseResult += lines.Sum(
                        x => x.Debit - x.Credit);
                    break;
            }
        }

        var accumulatedResult = revenueResult - expenseResult;
        var rightSide =
            liabilityTotal + equityTotal + accumulatedResult;

        return new BalanceSheetReportResponse(
            asOf,
            assetTotal,
            liabilityTotal,
            equityTotal,
            accumulatedResult,
            rightSide,
            assetTotal - rightSide,
            rows);
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

    private static readonly string[] PersianMonthNames =
    [
        "فروردین",
        "اردیبهشت",
        "خرداد",
        "تیر",
        "مرداد",
        "شهریور",
        "مهر",
        "آبان",
        "آذر",
        "دی",
        "بهمن",
        "اسفند"
    ];

    private static bool IsStandardPersianFiscalYear(
        FiscalYear fiscalYear)
    {
        var calendar = new PersianCalendar();

        var expectedStart = DateOnly.FromDateTime(
            calendar.ToDateTime(
                fiscalYear.PersianYear,
                1,
                1,
                0,
                0,
                0,
                0));

        var expectedNextStart = DateOnly.FromDateTime(
            calendar.ToDateTime(
                fiscalYear.PersianYear + 1,
                1,
                1,
                0,
                0,
                0,
                0));

        return fiscalYear.StartDate == expectedStart &&
               fiscalYear.EndDate == expectedNextStart.AddDays(-1);
    }

    private async Task<IReadOnlyList<FiscalPeriod>>
        EnsureStandardFiscalPeriodsCoreAsync(
            Guid companyId,
            FiscalYear fiscalYear,
            CancellationToken cancellationToken)
    {
        var existing = await db.FiscalPeriods
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYear.Id)
            .ToArrayAsync(cancellationToken);

        var byNumber = existing.ToDictionary(
            x => x.PeriodNumber);

        var calendar = new PersianCalendar();

        for (var month = 1; month <= 12; month++)
        {
            if (byNumber.ContainsKey(month))
            {
                continue;
            }

            var start = DateOnly.FromDateTime(
                calendar.ToDateTime(
                    fiscalYear.PersianYear,
                    month,
                    1,
                    0,
                    0,
                    0,
                    0));

            DateOnly nextStart;

            if (month == 12)
            {
                nextStart = DateOnly.FromDateTime(
                    calendar.ToDateTime(
                        fiscalYear.PersianYear + 1,
                        1,
                        1,
                        0,
                        0,
                        0,
                        0));
            }
            else
            {
                nextStart = DateOnly.FromDateTime(
                    calendar.ToDateTime(
                        fiscalYear.PersianYear,
                        month + 1,
                        1,
                        0,
                        0,
                        0,
                        0));
            }

            var period = new FiscalPeriod
            {
                CompanyId = companyId,
                FiscalYearId = fiscalYear.Id,
                PeriodNumber = month,
                Name = PersianMonthNames[month - 1],
                StartDate = start,
                EndDate = nextStart.AddDays(-1)
            };

            db.FiscalPeriods.Add(period);
            byNumber[month] = period;
        }

        await db.SaveChangesAsync(cancellationToken);

        return byNumber.Values
            .OrderBy(x => x.PeriodNumber)
            .ToArray();
    }

    private async Task EnsurePostingPeriodIsOpenAsync(
        Guid companyId,
        Guid fiscalYearId,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var periods = await db.FiscalPeriods
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalYearId == fiscalYearId)
            .ToArrayAsync(cancellationToken);

        if (periods.Length == 0)
        {
            return;
        }

        var period = periods.FirstOrDefault(
            x =>
                x.StartDate <= documentDate &&
                x.EndDate >= documentDate);

        if (period is null)
        {
            throw new InvalidOperationException(
                "No fiscal period covers the document date.");
        }

        if (period.IsClosed)
        {
            throw new InvalidOperationException(
                $"Fiscal period '{period.Name}' is closed.");
        }
    }

    private static HashSet<Guid> GetDescendantAccountIds(
        Guid rootAccountId,
        IReadOnlyList<LedgerAccount> accounts)
    {
        var result = new HashSet<Guid> { rootAccountId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootAccountId);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();

            foreach (var child in accounts.Where(
                         x => x.ParentId == parentId))
            {
                if (result.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return result;
    }

    private static void ValidateDateRange(
        DateOnly? from,
        DateOnly? to)
    {
        if (from is DateOnly fromDate &&
            to is DateOnly toDate &&
            fromDate > toDate)
        {
            throw new ArgumentException(
                "Report start date cannot be after end date.");
        }
    }

    private static void ValidateDetailAccountFields(
        string code,
        string name)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Detail code and name are required.");
        }
    }

    private static DetailAccountView ToDetailAccountView(
        DetailAccount detail,
        long revision,
        DateTimeOffset updatedAt)
    {
        return new DetailAccountView(
            detail.Id,
            detail.CompanyId,
            detail.Code,
            detail.Name,
            detail.Type,
            detail.NationalId,
            detail.IsActive,
            revision,
            updatedAt);
    }

    private async Task<FiscalYear> ResolveFiscalYearAsync(
        Guid companyId,
        Guid? fiscalYearId,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        FiscalYear? fiscalYear;

        if (fiscalYearId is Guid selectedId)
        {
            fiscalYear = await db.FiscalYears.FirstOrDefaultAsync(
                x => x.Id == selectedId && x.CompanyId == companyId,
                cancellationToken);

            if (fiscalYear is null)
            {
                throw new ArgumentException(
                    "Selected fiscal year does not exist in this company.");
            }
        }
        else
        {
            fiscalYear = await db.FiscalYears
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.StartDate <= documentDate &&
                    x.EndDate >= documentDate)
                .OrderByDescending(x => x.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (fiscalYear is null)
        {
            throw new InvalidOperationException(
                "No fiscal year covers the document date.");
        }

        if (documentDate < fiscalYear.StartDate ||
            documentDate > fiscalYear.EndDate)
        {
            throw new ArgumentException(
                "Document date is outside the selected fiscal year.");
        }

        return fiscalYear;
    }

    private async Task<string> GenerateJournalNumberAsync(
        Guid companyId,
        FiscalYear fiscalYear,
        CancellationToken cancellationToken)
    {
        var prefix =
            fiscalYear.PersianYear.ToString(CultureInfo.InvariantCulture) + "/";

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
