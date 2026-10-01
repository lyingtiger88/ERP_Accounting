using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class DetailAccountSyncTests
{
    [Fact]
    public async Task StaleRevision_ReturnsConflict_AndRetryCanApply()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);
        var entityId = Guid.NewGuid();

        var created = await service.SyncDetailAccountAsync(
            fixture.Company.Id,
            new SyncDetailAccountRequest(
                "change-create",
                entityId,
                "10001",
                "مشتری اولیه",
                DetailAccountType.Customer,
                null,
                true,
                0));

        Assert.Equal("Applied", created.Outcome);
        Assert.NotNull(created.Entity);
        Assert.Equal(1, created.Entity!.Revision);

        var deviceB = await service.SyncDetailAccountAsync(
            fixture.Company.Id,
            new SyncDetailAccountRequest(
                "change-device-b",
                entityId,
                "10001",
                "ویرایش دستگاه B",
                DetailAccountType.Customer,
                null,
                true,
                1));

        Assert.Equal("Applied", deviceB.Outcome);
        Assert.Equal(2, deviceB.Entity!.Revision);

        var staleDeviceA = await service.SyncDetailAccountAsync(
            fixture.Company.Id,
            new SyncDetailAccountRequest(
                "change-device-a-stale",
                entityId,
                "10001",
                "ویرایش آفلاین دستگاه A",
                DetailAccountType.Customer,
                null,
                true,
                1));

        Assert.Equal("Conflict", staleDeviceA.Outcome);
        Assert.NotNull(staleDeviceA.Conflict);
        Assert.Equal(1, staleDeviceA.Conflict!.BaseRevision);
        Assert.Equal(2, staleDeviceA.Conflict.Server!.Revision);
        Assert.Equal(
            "ویرایش دستگاه B",
            staleDeviceA.Conflict.Server.Name);

        var retryLocal = await service.SyncDetailAccountAsync(
            fixture.Company.Id,
            new SyncDetailAccountRequest(
                "change-device-a-retry",
                entityId,
                "10001",
                "ویرایش آفلاین دستگاه A",
                DetailAccountType.Customer,
                null,
                true,
                2));

        Assert.Equal("Applied", retryLocal.Outcome);
        Assert.Equal(3, retryLocal.Entity!.Revision);
        Assert.Equal(
            "ویرایش آفلاین دستگاه A",
            retryLocal.Entity.Name);
    }

    [Fact]
    public async Task RepeatedChangeId_IsIdempotent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);
        var entityId = Guid.NewGuid();

        var request = new SyncDetailAccountRequest(
            "same-change-id",
            entityId,
            "20001",
            "تأمین‌کننده نمونه",
            DetailAccountType.Supplier,
            null,
            true,
            0);

        var first = await service.SyncDetailAccountAsync(
            fixture.Company.Id,
            request);

        var second = await service.SyncDetailAccountAsync(
            fixture.Company.Id,
            request);

        Assert.Equal("Applied", first.Outcome);
        Assert.False(first.Duplicate);
        Assert.Equal(1, first.Entity!.Revision);

        Assert.Equal("Applied", second.Outcome);
        Assert.True(second.Duplicate);
        Assert.Equal(1, second.Entity!.Revision);

        var count = await fixture.Db.DetailAccounts.CountAsync(
            x => x.Id == entityId);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DetailPull_UsesMonotonicCursor()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        for (var index = 1; index <= 3; index++)
        {
            await service.SyncDetailAccountAsync(
                fixture.Company.Id,
                new SyncDetailAccountRequest(
                    $"create-{index}",
                    Guid.NewGuid(),
                    $"30{index:000}",
                    $"تفصیلی {index}",
                    DetailAccountType.Other,
                    null,
                    true,
                    0));
        }

        var firstPage = await service.PullDetailAccountChangesAsync(
            fixture.Company.Id,
            0,
            2);

        Assert.Equal(2, firstPage.Changes.Count);
        Assert.True(firstPage.HasMore);
        Assert.True(firstPage.NextCursor > 0);

        var secondPage = await service.PullDetailAccountChangesAsync(
            fixture.Company.Id,
            firstPage.NextCursor,
            2);

        Assert.Single(secondPage.Changes);
        Assert.False(secondPage.HasMore);
        Assert.True(secondPage.NextCursor > firstPage.NextCursor);
    }

    [Fact]
    public async Task PostedJournal_CanBeReversedOnce_WithAuditTrail()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        await service.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await service.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var cash = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1110");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "4100");

        var today = DateOnly.FromDateTime(DateTime.Today);

        var journal = await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                today,
                "فروش نقدی تست",
                new[]
                {
                    new CreateJournalLineRequest(
                        cash.Id,
                        "دریافت نقدی",
                        10_000_000m,
                        0m),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        "درآمد فروش",
                        0m,
                        10_000_000m)
                },
                fiscalYear.Id));

        var reversal = await service.ReverseJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            journal.Id,
            new ReverseJournalRequest(
                today,
                "اصلاح سند تست",
                fiscalYear.Id));

        Assert.Equal(journal.Id, reversal.OriginalJournalEntryId);
        Assert.NotEqual(journal.Id, reversal.ReversalJournalEntryId);
        Assert.NotEqual(journal.Number, reversal.ReversalNumber);

        var link = await fixture.Db.JournalReversalLinks.SingleAsync();
        Assert.Equal(journal.Id, link.OriginalJournalEntryId);
        Assert.Equal(
            reversal.ReversalJournalEntryId,
            link.ReversalJournalEntryId);

        var trialBalance = await service.GetTrialBalanceAsync(
            fixture.Company.Id);

        Assert.Equal(
            0m,
            trialBalance.Single(x => x.AccountId == cash.Id).Balance);
        Assert.Equal(
            0m,
            trialBalance.Single(x => x.AccountId == revenue.Id).Balance);

        var audit = await service.GetAuditLogsAsync(
            fixture.Company.Id);

        Assert.Contains(audit, x =>
            x.EntityId == journal.Id &&
            x.Action == "POST");
        Assert.Contains(audit, x =>
            x.EntityId == journal.Id &&
            x.Action == "REVERSE");
        Assert.Contains(audit, x =>
            x.EntityId == reversal.ReversalJournalEntryId &&
            x.Action == "REVERSAL_POST");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReverseJournalAsync(
                fixture.Company.Id,
                fixture.User.Id,
                journal.Id,
                new ReverseJournalRequest(
                    today,
                    "تلاش تکراری",
                    fiscalYear.Id)));
    }

    [Fact]
    public async Task Reports_RespectDateRange()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        await service.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await service.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var cash = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1110");

        var assetsGroup = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1000");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "4100");

        var firstDate = fiscalYear.StartDate.AddDays(10);
        var secondDate = fiscalYear.StartDate.AddDays(20);

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                firstDate,
                "سند اول",
                new[]
                {
                    new CreateJournalLineRequest(
                        cash.Id,
                        null,
                        1_000m,
                        0m),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        null,
                        0m,
                        1_000m)
                },
                fiscalYear.Id));

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                secondDate,
                "سند دوم",
                new[]
                {
                    new CreateJournalLineRequest(
                        cash.Id,
                        null,
                        2_000m,
                        0m),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        null,
                        0m,
                        2_000m)
                },
                fiscalYear.Id));

        var journals = await service.GetJournalEntriesAsync(
            fixture.Company.Id,
            firstDate,
            firstDate);

        Assert.Single(journals);
        Assert.Equal("سند اول", journals[0].Description);

        var ledger = await service.GetGeneralLedgerAsync(
            fixture.Company.Id,
            cash.Id,
            secondDate,
            secondDate);

        Assert.Single(ledger.Rows);
        Assert.Equal(1_000m, ledger.OpeningBalance);
        Assert.Equal(2_000m, ledger.DebitTurnover);
        Assert.Equal(3_000m, ledger.ClosingBalance);
        Assert.Equal(2_000m, ledger.Rows[0].Debit);
        Assert.Equal(3_000m, ledger.Rows[0].RunningBalance);

        var trial = await service.GetTrialBalanceAsync(
            fixture.Company.Id,
            secondDate,
            secondDate);

        var cashTrial =
            trial.Single(x => x.AccountId == cash.Id);
        var revenueTrial =
            trial.Single(x => x.AccountId == revenue.Id);

        Assert.Equal(1_000m, cashTrial.OpeningBalance);
        Assert.Equal(2_000m, cashTrial.DebitTurnover);
        Assert.Equal(3_000m, cashTrial.Balance);

        Assert.Equal(-1_000m, revenueTrial.OpeningBalance);
        Assert.Equal(2_000m, revenueTrial.CreditTurnover);
        Assert.Equal(-3_000m, revenueTrial.Balance);

        var assetsTrial =
            trial.Single(x => x.AccountId == assetsGroup.Id);

        Assert.Equal(1_000m, assetsTrial.OpeningBalance);
        Assert.Equal(2_000m, assetsTrial.DebitTurnover);
        Assert.Equal(3_000m, assetsTrial.Balance);
        Assert.False(assetsTrial.IsPostable);
        Assert.Equal("گروه", assetsTrial.LevelTitle);

        var assetsLedger = await service.GetGeneralLedgerAsync(
            fixture.Company.Id,
            assetsGroup.Id,
            secondDate,
            secondDate);

        Assert.Equal(1_000m, assetsLedger.OpeningBalance);
        Assert.Equal(2_000m, assetsLedger.DebitTurnover);
        Assert.Equal(3_000m, assetsLedger.ClosingBalance);
        Assert.Single(assetsLedger.Rows);
        Assert.Equal(cash.Id, assetsLedger.Rows[0].AccountId);
    }

    [Fact]
    public async Task InvalidReportDateRange_IsRejected()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        var from = new DateOnly(2026, 2, 2);
        var to = new DateOnly(2026, 2, 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetJournalEntriesAsync(
                fixture.Company.Id,
                from,
                to));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetTrialBalanceAsync(
                fixture.Company.Id,
                from,
                to));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetGeneralLedgerAsync(
                fixture.Company.Id,
                null,
                from,
                to));
    }

    [Fact]
    public async Task ClosedFiscalPeriod_BlocksPosting_UntilReopened()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        await service.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await service.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var periods = await service.GetFiscalPeriodsAsync(
            fixture.Company.Id,
            fiscalYear.Id);

        Assert.Equal(12, periods.Count);

        var firstPeriod = periods[0];

        await service.SetFiscalPeriodClosedAsync(
            fixture.Company.Id,
            fixture.User.Id,
            firstPeriod.Id,
            true);

        var cash = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1110");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "4100");

        var request = new CreateJournalRequest(
            null,
            firstPeriod.StartDate,
            "سند داخل دوره بسته",
            new[]
            {
                new CreateJournalLineRequest(
                    cash.Id,
                    null,
                    500m,
                    0m),
                new CreateJournalLineRequest(
                    revenue.Id,
                    null,
                    0m,
                    500m)
            },
            fiscalYear.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PostJournalAsync(
                fixture.Company.Id,
                fixture.User.Id,
                request));

        await service.SetFiscalPeriodClosedAsync(
            fixture.Company.Id,
            fixture.User.Id,
            firstPeriod.Id,
            false);

        var posted = await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            request);

        Assert.Equal(
            firstPeriod.StartDate,
            posted.DocumentDate);

        var audit = await service.GetAuditLogsAsync(
            fixture.Company.Id);

        Assert.Contains(
            audit,
            x => x.EntityId == firstPeriod.Id &&
                 x.Action == "FISCAL_PERIOD_CLOSE");
        Assert.Contains(
            audit,
            x => x.EntityId == firstPeriod.Id &&
                 x.Action == "FISCAL_PERIOD_REOPEN");
    }

    [Fact]
    public async Task ProfitLoss_AndBalanceSheet_StayConsistent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        await service.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await service.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var cash = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1110");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "4100");

        var date = fiscalYear.StartDate.AddDays(5);

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                date,
                "فروش نقدی",
                new[]
                {
                    new CreateJournalLineRequest(
                        cash.Id,
                        null,
                        10_000m,
                        0m),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        null,
                        0m,
                        10_000m)
                },
                fiscalYear.Id));

        var profitLoss = await service.GetProfitLossAsync(
            fixture.Company.Id,
            fiscalYear.StartDate,
            date);

        Assert.Equal(10_000m, profitLoss.RevenueTotal);
        Assert.Equal(0m, profitLoss.ExpenseTotal);
        Assert.Equal(10_000m, profitLoss.NetProfit);

        var balanceSheet = await service.GetBalanceSheetAsync(
            fixture.Company.Id,
            date);

        Assert.Equal(10_000m, balanceSheet.AssetTotal);
        Assert.Equal(0m, balanceSheet.LiabilityTotal);
        Assert.Equal(0m, balanceSheet.EquityTotal);
        Assert.Equal(
            10_000m,
            balanceSheet.AccumulatedResult);
        Assert.Equal(
            10_000m,
            balanceSheet.RightSideTotal);
        Assert.Equal(0m, balanceSheet.Difference);
    }

    [Fact]
    public async Task DetailLedger_CarriesOpeningAndRunningBalance()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        await service.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await service.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var receivable = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1200");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "4100");

        var detail = await service.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "CUS-001",
                "مشتری تست",
                DetailAccountType.Customer,
                null));

        var firstDate = fiscalYear.StartDate.AddDays(2);
        var secondDate = fiscalYear.StartDate.AddDays(12);

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                firstDate,
                "فروش نسیه اول",
                new[]
                {
                    new CreateJournalLineRequest(
                        receivable.Id,
                        null,
                        1_000m,
                        0m,
                        detail.Id),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        null,
                        0m,
                        1_000m)
                },
                fiscalYear.Id));

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                secondDate,
                "فروش نسیه دوم",
                new[]
                {
                    new CreateJournalLineRequest(
                        receivable.Id,
                        null,
                        2_000m,
                        0m,
                        detail.Id),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        null,
                        0m,
                        2_000m)
                },
                fiscalYear.Id));

        var report = await service.GetDetailLedgerAsync(
            fixture.Company.Id,
            detail.Id,
            secondDate,
            secondDate);

        Assert.Equal(1_000m, report.OpeningBalance);
        Assert.Equal(2_000m, report.DebitTurnover);
        Assert.Equal(0m, report.CreditTurnover);
        Assert.Equal(3_000m, report.ClosingBalance);
        Assert.Single(report.Rows);
        Assert.Equal(3_000m, report.Rows[0].RunningBalance);
        Assert.Equal("CUS-001", report.Rows[0].DetailCode);
    }

    [Fact]
    public async Task FiscalYearFinalization_ClosesTemporaryAccounts_AndCanBeReopened()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = new AccountingService(fixture.Db);

        await service.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await service.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var cash = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "1110");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "4100");

        var expense = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "5200");

        var retained = await fixture.Db.Accounts.SingleAsync(
            x => x.CompanyId == fixture.Company.Id &&
                 x.Code == "3200");

        var saleDate = fiscalYear.StartDate.AddDays(3);
        var expenseDate = fiscalYear.StartDate.AddDays(4);

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                saleDate,
                "فروش",
                new[]
                {
                    new CreateJournalLineRequest(
                        cash.Id,
                        null,
                        10_000m,
                        0m),
                    new CreateJournalLineRequest(
                        revenue.Id,
                        null,
                        0m,
                        10_000m)
                },
                fiscalYear.Id));

        await service.PostJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateJournalRequest(
                null,
                expenseDate,
                "هزینه",
                new[]
                {
                    new CreateJournalLineRequest(
                        expense.Id,
                        null,
                        4_000m,
                        0m),
                    new CreateJournalLineRequest(
                        cash.Id,
                        null,
                        0m,
                        4_000m)
                },
                fiscalYear.Id));

        var before = await service.GetProfitLossAsync(
            fixture.Company.Id,
            fiscalYear.StartDate,
            fiscalYear.EndDate);

        Assert.Equal(10_000m, before.RevenueTotal);
        Assert.Equal(4_000m, before.ExpenseTotal);
        Assert.Equal(6_000m, before.NetProfit);

        var finalized = await service.FinalizeFiscalYearAsync(
            fixture.Company.Id,
            fixture.User.Id,
            fiscalYear.Id,
            new FinalizeFiscalYearRequest(retained.Id));

        Assert.False(finalized.AlreadyFinalized);
        Assert.NotNull(finalized.ClosingJournalEntryId);
        Assert.NotNull(finalized.ClosingJournalNumber);
        Assert.Equal(6_000m, finalized.NetResult);

        var closedYear = await fixture.Db.FiscalYears
            .AsNoTracking()
            .SingleAsync(x => x.Id == fiscalYear.Id);

        Assert.True(closedYear.IsClosed);
        Assert.All(
            await fixture.Db.FiscalPeriods
                .AsNoTracking()
                .Where(x => x.FiscalYearId == fiscalYear.Id)
                .ToArrayAsync(),
            period => Assert.True(period.IsClosed));

        var after = await service.GetProfitLossAsync(
            fixture.Company.Id,
            fiscalYear.StartDate,
            fiscalYear.EndDate);

        Assert.Equal(10_000m, after.RevenueTotal);
        Assert.Equal(4_000m, after.ExpenseTotal);
        Assert.Equal(6_000m, after.NetProfit);

        var trial = await service.GetTrialBalanceAsync(
            fixture.Company.Id,
            fiscalYear.StartDate,
            fiscalYear.EndDate);

        Assert.Equal(
            0m,
            trial.Single(x => x.AccountId == revenue.Id).Balance);
        Assert.Equal(
            0m,
            trial.Single(x => x.AccountId == expense.Id).Balance);
        Assert.Equal(
            -6_000m,
            trial.Single(x => x.AccountId == retained.Id).Balance);

        var balanceSheet = await service.GetBalanceSheetAsync(
            fixture.Company.Id,
            fiscalYear.EndDate);

        Assert.Equal(6_000m, balanceSheet.AssetTotal);
        Assert.Equal(6_000m, balanceSheet.EquityTotal);
        Assert.Equal(0m, balanceSheet.AccumulatedResult);
        Assert.Equal(0m, balanceSheet.Difference);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PostJournalAsync(
                fixture.Company.Id,
                fixture.User.Id,
                new CreateJournalRequest(
                    null,
                    fiscalYear.EndDate,
                    "ثبت بعد از بستن",
                    new[]
                    {
                        new CreateJournalLineRequest(
                            cash.Id,
                            null,
                            1m,
                            0m),
                        new CreateJournalLineRequest(
                            revenue.Id,
                            null,
                            0m,
                            1m)
                    },
                    fiscalYear.Id)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReverseJournalAsync(
                fixture.Company.Id,
                fixture.User.Id,
                finalized.ClosingJournalEntryId!.Value,
                new ReverseJournalRequest(
                    fiscalYear.EndDate,
                    "برگشت مستقیم غیرمجاز",
                    fiscalYear.Id)));

        var reopened = await service.ReopenFinalizedFiscalYearAsync(
            fixture.Company.Id,
            fixture.User.Id,
            fiscalYear.Id);

        Assert.NotNull(reopened.ReversalJournalEntryId);
        Assert.NotNull(reopened.ReversalJournalNumber);

        var reopenedYear = await fixture.Db.FiscalYears
            .AsNoTracking()
            .SingleAsync(x => x.Id == fiscalYear.Id);

        Assert.False(reopenedYear.IsClosed);
        Assert.False(
            await fixture.Db.FiscalYearClosings
                .AsNoTracking()
                .AnyAsync(x => x.FiscalYearId == fiscalYear.Id));

        var lastPeriod = await fixture.Db.FiscalPeriods
            .AsNoTracking()
            .Where(x => x.FiscalYearId == fiscalYear.Id)
            .OrderByDescending(x => x.EndDate)
            .FirstAsync();

        Assert.False(lastPeriod.IsClosed);

        var reopenedProfitLoss = await service.GetProfitLossAsync(
            fixture.Company.Id,
            fiscalYear.StartDate,
            fiscalYear.EndDate);

        Assert.Equal(10_000m, reopenedProfitLoss.RevenueTotal);
        Assert.Equal(4_000m, reopenedProfitLoss.ExpenseTotal);
        Assert.Equal(6_000m, reopenedProfitLoss.NetProfit);

        var reopenedBalanceSheet =
            await service.GetBalanceSheetAsync(
                fixture.Company.Id,
                fiscalYear.EndDate);

        Assert.Equal(6_000m, reopenedBalanceSheet.AssetTotal);
        Assert.Equal(0m, reopenedBalanceSheet.EquityTotal);
        Assert.Equal(
            6_000m,
            reopenedBalanceSheet.AccumulatedResult);
        Assert.Equal(0m, reopenedBalanceSheet.Difference);

        var audit = await service.GetAuditLogsAsync(
            fixture.Company.Id,
            limit: 500);

        Assert.Contains(
            audit,
            x => x.EntityId == fiscalYear.Id &&
                 x.Action == "FISCAL_YEAR_FINALIZE");
        Assert.Contains(
            audit,
            x => x.EntityId == fiscalYear.Id &&
                 x.Action == "FISCAL_YEAR_FINALIZATION_REOPEN");
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            SqliteConnection connection,
            AppDbContext db,
            Company company,
            AppUser user)
        {
            Connection = connection;
            Db = db;
            Company = company;
            User = user;
        }

        private SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public Company Company { get; }
        public AppUser User { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var company = new Company
            {
                Name = "Test Company"
            };

            db.Companies.Add(company);

            var user = new AppUser
            {
                CompanyId = company.Id,
                Username = "owner",
                DisplayName = "Owner",
                PasswordHash = "test-hash",
                Role = UserRole.Owner
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return new TestFixture(
                connection,
                db,
                company,
                user);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
