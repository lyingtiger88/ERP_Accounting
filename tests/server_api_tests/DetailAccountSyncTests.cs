using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.SalesInventory;
using ERPAccounting.Api.Treasury;
using ERPAccounting.Api.Reporting;
using ERPAccounting.Api.Security;
using Microsoft.AspNetCore.Identity;
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

        var yearViews = await service.GetFiscalYearsAsync(
            fixture.Company.Id);

        var finalizedView = yearViews.Single(
            x => x.Id == fiscalYear.Id);

        Assert.True(finalizedView.IsFinalized);
        Assert.True(finalizedView.IsClosed);
        Assert.Equal(
            finalized.ClosingJournalNumber,
            finalizedView.ClosingJournalNumber);
        Assert.Equal(6_000m, finalizedView.NetResult);

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

        var reopenedYearView = (await service.GetFiscalYearsAsync(
            fixture.Company.Id))
            .Single(x => x.Id == fiscalYear.Id);

        Assert.False(reopenedYearView.IsFinalized);
        Assert.Null(reopenedYearView.ClosingJournalNumber);
        Assert.Null(reopenedYearView.NetResult);

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

    [Fact]
    public async Task PostedSale_UpdatesStock_AndCreatesBalancedAccountingJournal()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "SKU-001",
                "کالای تست",
                "626000000001",
                "عدد",
                ProductKind.Inventory,
                true,
                15_000m,
                10_000m));

        var saleDate = fiscalYear.StartDate.AddDays(5);

        await store.AdjustStockAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateStockAdjustmentRequest(
                warehouse.Id,
                product.Id,
                saleDate,
                10m,
                10_000m,
                "موجودی اولیه تست"));

        var invoice = await store.CreateSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateSalesInvoiceRequest(
                fiscalYear.Id,
                saleDate,
                warehouse.Id,
                null,
                SalesPaymentType.Cash,
                "فروش نقدی تست",
                new[]
                {
                    new SalesInvoiceLineRequest(
                        product.Id,
                        2m,
                        15_000m,
                        0m,
                        3_000m)
                }));

        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status);
        Assert.Equal(33_000m, invoice.GrandTotal);

        var posted = await store.PostSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            invoice.Id);

        Assert.Equal(33_000m, posted.GrandTotal);
        Assert.Equal(20_000m, posted.CostTotal);

        var savedInvoice = await fixture.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(
            SalesInvoiceStatus.Posted,
            savedInvoice.Status);
        Assert.Equal(
            posted.AccountingJournalEntryId,
            savedInvoice.AccountingJournalEntryId);

        var stock = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(8m, stock.Quantity);
        Assert.Equal(10_000m, stock.AverageCost);
        Assert.Equal(80_000m, stock.InventoryValue);

        var journal = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.Id == posted.AccountingJournalEntryId);

        Assert.Equal(JournalStatus.Posted, journal.Status);
        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            53_000m,
            journal.Lines.Sum(x => x.Debit));
        Assert.Equal(
            53_000m,
            journal.Lines.Sum(x => x.Credit));

        var settings = await fixture.Db.SalesInventorySettings
            .AsNoTracking()
            .SingleAsync(
                x => x.CompanyId == fixture.Company.Id);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.CashAccountId &&
                x.Debit == 33_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.SalesRevenueAccountId &&
                x.Credit == 30_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.SalesTaxPayableAccountId &&
                x.Credit == 3_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.CostOfGoodsSoldAccountId &&
                x.Debit == 20_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.InventoryAccountId &&
                x.Credit == 20_000m);

        var audit = await accounting.GetAuditLogsAsync(
            fixture.Company.Id,
            limit: 500);

        Assert.Contains(
            audit,
            x =>
                x.EntityId == invoice.Id &&
                x.Action == "SALES_INVOICE_POST");
    }

    [Fact]
    public async Task PostedPurchase_ReceivesStock_AndCreatesBalancedAccountingJournal()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var supplier = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "SUP-001",
                "تامین‌کننده تست",
                DetailAccountType.Supplier,
                null));

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "PUR-001",
                "کالای خرید تست",
                "626000000101",
                "عدد",
                ProductKind.Inventory,
                true,
                15_000m,
                10_000m));

        var date = fiscalYear.StartDate.AddDays(7);

        var receipt = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseReceiptRequest(
                fiscalYear.Id,
                date,
                warehouse.Id,
                supplier.Id,
                PurchasePaymentType.Credit,
                "خرید تست",
                new[]
                {
                    new PurchaseReceiptLineRequest(
                        product.Id,
                        5m,
                        10_000m,
                        0m,
                        5_000m)
                }));

        Assert.Equal(PurchaseReceiptStatus.Draft, receipt.Status);
        Assert.Equal(55_000m, receipt.GrandTotal);

        var posted = await store.PostPurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            receipt.Id);

        Assert.Equal(55_000m, posted.GrandTotal);

        var stock = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(5m, stock.Quantity);
        Assert.Equal(10_000m, stock.AverageCost);
        Assert.Equal(50_000m, stock.InventoryValue);

        var journal = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.Id == posted.AccountingJournalEntryId);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        var settings = await fixture.Db.SalesInventorySettings
            .AsNoTracking()
            .SingleAsync(
                x => x.CompanyId == fixture.Company.Id);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.InventoryAccountId &&
                x.Debit == 50_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId ==
                    settings.PurchaseTaxReceivableAccountId &&
                x.Debit == 5_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.PayablesAccountId &&
                x.Credit == 55_000m);

        var payableLine = journal.Lines.Single(
            x => x.AccountId == settings.PayablesAccountId);

        var dimension = await fixture.Db.JournalLineDimensions
            .AsNoTracking()
            .SingleAsync(
                x => x.JournalLineId == payableLine.Id);

        Assert.Equal(supplier.Id, dimension.DetailAccountId);
    }

    [Fact]
    public async Task AuthSession_Persists_RotatesRefresh_AndRejectsReplay()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var hasher = new PasswordHasher<AppUser>();
        const string password = "Strong-Test-Password-123!";
        fixture.User.PasswordHash = hasher.HashPassword(
            fixture.User,
            password);
        await fixture.Db.SaveChangesAsync();

        var auth = new AuthService(
            fixture.Db,
            hasher);

        var login = await auth.LoginAsync(
            new LoginRequest(
                fixture.User.Username,
                password,
                "CI Device",
                "test"));

        Assert.NotNull(login);
        Assert.NotEmpty(login!.AccessToken);
        Assert.NotEmpty(login.RefreshToken);
        Assert.True(login.RefreshExpiresAt > login.ExpiresAt);

        var resolved = await auth.ResolveAsync(login.AccessToken);
        Assert.Equal(fixture.User.Id, resolved?.Id);

        var restartedAuth = new AuthService(
            fixture.Db,
            hasher);
        var afterRestart = await restartedAuth.ResolveAsync(
            login.AccessToken);
        Assert.Equal(fixture.User.Id, afterRestart?.Id);

        var refreshed = await restartedAuth.RefreshAsync(
            new RefreshSessionRequest(login.RefreshToken));

        Assert.NotNull(refreshed);
        Assert.NotEqual(login.AccessToken, refreshed!.AccessToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        Assert.Null(
            await restartedAuth.ResolveAsync(login.AccessToken));

        Assert.Null(
            await restartedAuth.RefreshAsync(
                new RefreshSessionRequest(login.RefreshToken)));

        Assert.Equal(
            fixture.User.Id,
            (await restartedAuth.ResolveAsync(
                refreshed.AccessToken))?.Id);

        Assert.True(
            await restartedAuth.RevokeAsync(
                refreshed.AccessToken,
                refreshed.RefreshToken));

        Assert.Null(
            await restartedAuth.ResolveAsync(
                refreshed.AccessToken));
        Assert.Null(
            await restartedAuth.RefreshAsync(
                new RefreshSessionRequest(
                    refreshed.RefreshToken)));
    }

    [Fact]
    public async Task ReportsCenter_RespectsFromToDateRange_AndCalculatesProfit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);
        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);
        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "RPT-001",
                "کالای گزارش تست",
                null,
                "عدد",
                ProductKind.Inventory,
                true,
                20_000m,
                12_000m));

        var firstDate = fiscalYear.StartDate.AddDays(3);
        var secondDate = fiscalYear.StartDate.AddDays(15);

        await store.AdjustStockAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateStockAdjustmentRequest(
                warehouse.Id,
                product.Id,
                firstDate,
                10m,
                12_000m,
                "موجودی گزارش"));

        async Task PostSale(DateOnly date)
        {
            var invoice = await store.CreateSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.User.Id,
                new CreateSalesInvoiceRequest(
                    fiscalYear.Id,
                    date,
                    warehouse.Id,
                    null,
                    SalesPaymentType.Cash,
                    "فروش گزارش",
                    new[]
                    {
                        new SalesInvoiceLineRequest(
                            product.Id,
                            1m,
                            20_000m,
                            0m,
                            0m)
                    }));

            await store.PostSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.User.Id,
                invoice.Id);
        }

        await PostSale(firstDate);
        await PostSale(secondDate);

        var reporting = new ReportingService(fixture.Db);

        var all = await reporting.GetReportsCenterAsync(
            fixture.Company.Id,
            firstDate,
            secondDate);

        Assert.Equal(40_000m, all.Summary.NetSales);
        Assert.Equal(24_000m, all.Summary.CostOfGoodsSold);
        Assert.Equal(16_000m, all.Summary.GrossProfit);
        Assert.Equal(2m, all.Products.Single().NetSoldQuantity);

        var ranged = await reporting.GetReportsCenterAsync(
            fixture.Company.Id,
            secondDate,
            secondDate);

        Assert.Equal(secondDate, ranged.From);
        Assert.Equal(secondDate, ranged.To);
        Assert.Equal(20_000m, ranged.Summary.NetSales);
        Assert.Equal(12_000m, ranged.Summary.CostOfGoodsSold);
        Assert.Equal(8_000m, ranged.Summary.GrossProfit);
        Assert.Equal(
            1m,
            ranged.Products.Single().NetSoldQuantity);

        var inventory = ranged.Inventory.Single(
            x =>
                x.ProductId == product.Id &&
                x.WarehouseId == warehouse.Id);

        Assert.Equal(9m, inventory.OpeningQuantity);
        Assert.Equal(1m, inventory.OutQuantity);
        Assert.Equal(8m, inventory.ClosingQuantity);
        Assert.Equal(96_000m, inventory.ClosingValue);
    }

    [Fact]
    public async Task Treasury_ReceiptPaymentAndTransfer_CreateBalancedJournals()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var currency = new CurrencyAccountingService(
            fixture.Db,
            accounting);
        await currency.EnsureDefaultsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        var treasury = new TreasuryService(
            fixture.Db,
            accounting,
            currency);

        var ledgers = await fixture.Db.Accounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == fixture.Company.Id &&
                new[] { "1110", "1120", "1200", "2100" }
                    .Contains(x.Code))
            .ToDictionaryAsync(x => x.Code);

        var cashbox = await treasury.CreateAccountAsync(
            fixture.Company.Id,
            new CreateTreasuryAccountRequest(
                "CASH-01",
                "صندوق اصلی",
                TreasuryAccountType.Cashbox,
                ledgers["1110"].Id));

        var bank = await treasury.CreateAccountAsync(
            fixture.Company.Id,
            new CreateTreasuryAccountRequest(
                "BANK-01",
                "بانک اصلی",
                TreasuryAccountType.Bank,
                ledgers["1120"].Id));

        var customer = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "CUS-TR",
                "مشتری خزانه",
                DetailAccountType.Customer,
                null));

        var supplier = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "SUP-TR",
                "تامین‌کننده خزانه",
                DetailAccountType.Supplier,
                null));

        var date = fiscalYear.StartDate.AddDays(15);

        var receipt = await treasury.PostTransactionAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateTreasuryTransactionRequest(
                fiscalYear.Id,
                date,
                TreasuryTransactionType.Receipt,
                100_000m,
                "وصول از مشتری",
                ToTreasuryAccountId: cashbox.Id,
                CounterAccountId: ledgers["1200"].Id,
                DetailAccountId: customer.Id));

        var payment = await treasury.PostTransactionAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateTreasuryTransactionRequest(
                fiscalYear.Id,
                date.AddDays(1),
                TreasuryTransactionType.Payment,
                70_000m,
                "پرداخت به تامین‌کننده",
                FromTreasuryAccountId: cashbox.Id,
                CounterAccountId: ledgers["2100"].Id,
                DetailAccountId: supplier.Id));

        var transfer = await treasury.PostTransactionAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateTreasuryTransactionRequest(
                fiscalYear.Id,
                date.AddDays(2),
                TreasuryTransactionType.Transfer,
                20_000m,
                "انتقال صندوق به بانک",
                FromTreasuryAccountId: cashbox.Id,
                ToTreasuryAccountId: bank.Id));

        Assert.NotNull(receipt.AccountingJournalEntryId);
        Assert.NotNull(payment.AccountingJournalEntryId);
        Assert.NotNull(transfer.AccountingJournalEntryId);

        var journalIds = new[]
        {
            receipt.AccountingJournalEntryId!.Value,
            payment.AccountingJournalEntryId!.Value,
            transfer.AccountingJournalEntryId!.Value
        };

        var journals = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => journalIds.Contains(x.Id))
            .ToArrayAsync();

        Assert.Equal(3, journals.Length);
        Assert.All(
            journals,
            journal => Assert.Equal(
                journal.Lines.Sum(x => x.Debit),
                journal.Lines.Sum(x => x.Credit)));

        var receiptJournal = journals.Single(
            x => x.Id == receipt.AccountingJournalEntryId);
        Assert.Contains(
            receiptJournal.Lines,
            x =>
                x.AccountId == ledgers["1110"].Id &&
                x.Debit == 100_000m);
        Assert.Contains(
            receiptJournal.Lines,
            x =>
                x.AccountId == ledgers["1200"].Id &&
                x.Credit == 100_000m);

        var paymentJournal = journals.Single(
            x => x.Id == payment.AccountingJournalEntryId);
        Assert.Contains(
            paymentJournal.Lines,
            x =>
                x.AccountId == ledgers["2100"].Id &&
                x.Debit == 70_000m);
        Assert.Contains(
            paymentJournal.Lines,
            x =>
                x.AccountId == ledgers["1110"].Id &&
                x.Credit == 70_000m);

        var transferJournal = journals.Single(
            x => x.Id == transfer.AccountingJournalEntryId);
        Assert.Contains(
            transferJournal.Lines,
            x =>
                x.AccountId == ledgers["1120"].Id &&
                x.Debit == 20_000m);
        Assert.Contains(
            transferJournal.Lines,
            x =>
                x.AccountId == ledgers["1110"].Id &&
                x.Credit == 20_000m);
    }

    [Fact]
    public async Task PurchaseOrder_StatusWorkflow_HasNoStockOrAccountingEffect()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var supplier = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "SUP-PO",
                "تامین‌کننده سفارش خرید",
                DetailAccountType.Supplier,
                null));

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "PO-ITEM-001",
                "کالای سفارش خرید",
                "626000000303",
                "عدد",
                ProductKind.Inventory,
                true,
                20_000m,
                12_000m));

        var journalCountBefore =
            await fixture.Db.JournalEntries.CountAsync();
        var movementCountBefore =
            await fixture.Db.StockMovements.CountAsync();

        var order = await store.CreatePurchaseOrderAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseOrderRequest(
                fiscalYear.Id,
                fiscalYear.StartDate.AddDays(4),
                fiscalYear.StartDate.AddDays(10),
                supplier.Id,
                warehouse.Id,
                "سفارش خرید تست",
                new[]
                {
                    new PurchaseOrderLineRequest(
                        product.Id,
                        4m,
                        12_000m,
                        2_000m,
                        4_600m)
                }));

        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal(50_600m, order.GrandTotal);
        Assert.Equal(4m, order.Lines.Single().Quantity);
        Assert.Equal(0m, order.Lines.Single().ReceivedQuantity);

        var approved = await store.SetPurchaseOrderStatusAsync(
            fixture.Company.Id,
            fixture.User.Id,
            order.Id,
            PurchaseOrderStatus.Approved);

        Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedAt);

        var closed = await store.SetPurchaseOrderStatusAsync(
            fixture.Company.Id,
            fixture.User.Id,
            order.Id,
            PurchaseOrderStatus.Closed);

        Assert.Equal(PurchaseOrderStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);

        Assert.Equal(
            journalCountBefore,
            await fixture.Db.JournalEntries.CountAsync());
        Assert.Equal(
            movementCountBefore,
            await fixture.Db.StockMovements.CountAsync());
    }

    [Fact]
    public async Task PurchaseOrder_ReceiptsTrackOpenQuantity_AndAutoClose()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var supplier = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "SUP-PO-REC",
                "تامین‌کننده دریافت سفارش",
                DetailAccountType.Supplier,
                null));

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "PO-REC-001",
                "کالای دریافت سفارش",
                "626000000404",
                "عدد",
                ProductKind.Inventory,
                true,
                20_000m,
                12_000m));

        var date = fiscalYear.StartDate.AddDays(12);

        var order = await store.CreatePurchaseOrderAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseOrderRequest(
                fiscalYear.Id,
                date,
                date.AddDays(5),
                supplier.Id,
                warehouse.Id,
                "سفارش برای دریافت مرحله‌ای",
                new[]
                {
                    new PurchaseOrderLineRequest(
                        product.Id,
                        4m,
                        12_000m,
                        2_000m,
                        4_600m)
                }));

        order = await store.SetPurchaseOrderStatusAsync(
            fixture.Company.Id,
            fixture.User.Id,
            order.Id,
            PurchaseOrderStatus.Approved);

        var orderLine = order.Lines.Single();

        var firstReceipt = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseReceiptRequest(
                fiscalYear.Id,
                date.AddDays(1),
                warehouse.Id,
                supplier.Id,
                PurchasePaymentType.Credit,
                "دریافت اول",
                new[]
                {
                    new PurchaseReceiptLineRequest(
                        product.Id,
                        2m,
                        12_000m,
                        1_000m,
                        2_300m,
                        PurchaseOrderLineId: orderLine.Id)
                },
                PurchaseOrderId: order.Id));

        Assert.Equal(order.Id, firstReceipt.PurchaseOrderId);
        Assert.Equal(order.Number, firstReceipt.PurchaseOrderNumber);

        await store.PostPurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            firstReceipt.Id);

        var afterFirst = (await store.GetPurchaseOrdersAsync(
            fixture.Company.Id)).Single(x => x.Id == order.Id);

        Assert.Equal(PurchaseOrderStatus.Approved, afterFirst.Status);
        Assert.Equal(
            2m,
            afterFirst.Lines.Single().ReceivedQuantity);

        var secondReceipt = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseReceiptRequest(
                fiscalYear.Id,
                date.AddDays(2),
                warehouse.Id,
                supplier.Id,
                PurchasePaymentType.Credit,
                "دریافت دوم",
                new[]
                {
                    new PurchaseReceiptLineRequest(
                        product.Id,
                        2m,
                        12_000m,
                        1_000m,
                        2_300m,
                        PurchaseOrderLineId: orderLine.Id)
                },
                PurchaseOrderId: order.Id));

        await store.PostPurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            secondReceipt.Id);

        var completed = (await store.GetPurchaseOrdersAsync(
            fixture.Company.Id)).Single(x => x.Id == order.Id);

        Assert.Equal(PurchaseOrderStatus.Closed, completed.Status);
        Assert.Equal(
            4m,
            completed.Lines.Single().ReceivedQuantity);
        Assert.NotNull(completed.ClosedAt);

        var stock = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(4m, stock.Quantity);
        Assert.Equal(46_000m, stock.InventoryValue);
    }

    [Fact]
    public async Task PurchaseReturn_ReducesStock_AndReversesPurchaseAccounting()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var supplier = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "SUP-RET",
                "تامین‌کننده برگشت تست",
                DetailAccountType.Supplier,
                null));

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "PUR-RET-001",
                "کالای برگشت خرید تست",
                "626000000202",
                "عدد",
                ProductKind.Inventory,
                true,
                15_000m,
                10_000m));

        var date = fiscalYear.StartDate.AddDays(8);

        var receipt = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseReceiptRequest(
                fiscalYear.Id,
                date,
                warehouse.Id,
                supplier.Id,
                PurchasePaymentType.Credit,
                "خرید برای تست برگشت",
                new[]
                {
                    new PurchaseReceiptLineRequest(
                        product.Id,
                        5m,
                        10_000m,
                        0m,
                        5_000m)
                }));

        await store.PostPurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            receipt.Id);

        var purchaseReturn = await store.CreatePurchaseReturnAsync(
            fixture.Company.Id,
            fixture.User.Id,
            receipt.Id,
            new CreatePurchaseReturnRequest(
                date.AddDays(1),
                "برگشت دو عدد معیوب",
                new[]
                {
                    new PurchaseReturnLineRequest(
                        receipt.Lines.Single().Id,
                        2m)
                }));

        Assert.Equal(22_000m, purchaseReturn.GrandTotal);
        Assert.Equal(2_000m, purchaseReturn.TaxTotal);
        Assert.Equal(PurchaseReturnStatus.Posted, purchaseReturn.Status);

        var stock = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(3m, stock.Quantity);
        Assert.Equal(10_000m, stock.AverageCost);
        Assert.Equal(30_000m, stock.InventoryValue);

        var journal = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.Id == purchaseReturn.AccountingJournalEntryId);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        var settings = await fixture.Db.SalesInventorySettings
            .AsNoTracking()
            .SingleAsync(
                x => x.CompanyId == fixture.Company.Id);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.PayablesAccountId &&
                x.Debit == 22_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == settings.InventoryAccountId &&
                x.Credit == 20_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId ==
                    settings.PurchaseTaxReceivableAccountId &&
                x.Credit == 2_000m);

        var savedReceipt = await fixture.Db.PurchaseReceipts
            .AsNoTracking()
            .SingleAsync(x => x.Id == receipt.Id);

        Assert.Equal(
            PurchaseReceiptStatus.Posted,
            savedReceipt.Status);

        var finalReturn = await store.CreatePurchaseReturnAsync(
            fixture.Company.Id,
            fixture.User.Id,
            receipt.Id,
            new CreatePurchaseReturnRequest(
                date.AddDays(2),
                "برگشت مانده خرید",
                new[]
                {
                    new PurchaseReturnLineRequest(
                        receipt.Lines.Single().Id,
                        3m)
                }));

        Assert.Equal(33_000m, finalReturn.GrandTotal);

        savedReceipt = await fixture.Db.PurchaseReceipts
            .AsNoTracking()
            .SingleAsync(x => x.Id == receipt.Id);

        Assert.Equal(
            PurchaseReceiptStatus.Reversed,
            savedReceipt.Status);

        stock = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(0m, stock.Quantity);
        Assert.Equal(0m, stock.InventoryValue);
    }

    [Fact]
    public async Task WarehouseTransfer_PreservesTotalQuantityAndValue()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);
        await accounting.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);
        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var source = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var destination = await store.CreateWarehouseAsync(
            fixture.Company.Id,
            new CreateWarehouseRequest(
                "SECOND",
                "انبار دوم"));

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "TRF-001",
                "کالای انتقال تست",
                null,
                "عدد",
                ProductKind.Inventory,
                true,
                0m,
                2_500m));

        var fiscalYear = await accounting.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);
        var date = fiscalYear.StartDate.AddDays(8);

        await store.AdjustStockAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateStockAdjustmentRequest(
                source.Id,
                product.Id,
                date,
                10m,
                2_500m,
                "موجودی انتقال تست"));

        var transfer = await store.CreateWarehouseTransferAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateWarehouseTransferRequest(
                date,
                source.Id,
                destination.Id,
                "انتقال تست",
                new[]
                {
                    new WarehouseTransferLineRequest(
                        product.Id,
                        4m)
                }));

        await store.PostWarehouseTransferAsync(
            fixture.Company.Id,
            fixture.User.Id,
            transfer.Id);

        var balances = await store.GetStockBalancesAsync(
            fixture.Company.Id);

        var sourceBalance = balances.Single(
            x =>
                x.ProductId == product.Id &&
                x.WarehouseId == source.Id);
        var destinationBalance = balances.Single(
            x =>
                x.ProductId == product.Id &&
                x.WarehouseId == destination.Id);

        Assert.Equal(6m, sourceBalance.Quantity);
        Assert.Equal(4m, destinationBalance.Quantity);
        Assert.Equal(
            10m,
            sourceBalance.Quantity +
                destinationBalance.Quantity);
        Assert.Equal(
            25_000m,
            sourceBalance.InventoryValue +
                destinationBalance.InventoryValue);
    }

    [Fact]
    public async Task PartialSalesReturn_RestoresStock_AndCreatesBalancedAccountingJournal()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);
        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);
        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "RET-001",
                "کالای برگشت تست",
                null,
                "عدد",
                ProductKind.Inventory,
                true,
                20_000m,
                12_000m));

        var date = fiscalYear.StartDate.AddDays(9);

        await store.AdjustStockAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateStockAdjustmentRequest(
                warehouse.Id,
                product.Id,
                date,
                5m,
                12_000m,
                "موجودی برگشت تست"));

        var invoice = await store.CreateSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateSalesInvoiceRequest(
                fiscalYear.Id,
                date,
                warehouse.Id,
                null,
                SalesPaymentType.Cash,
                null,
                new[]
                {
                    new SalesInvoiceLineRequest(
                        product.Id,
                        2m,
                        20_000m,
                        0m,
                        4_000m)
                }));

        await store.PostSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            invoice.Id);

        var postedInvoice =
            (await store.GetSalesInvoicesAsync(
                fixture.Company.Id))
            .Single(x => x.Id == invoice.Id);

        var originalLine = postedInvoice.Lines.Single();

        var salesReturn = await store.CreateSalesReturnAsync(
            fixture.Company.Id,
            fixture.User.Id,
            invoice.Id,
            new CreateSalesReturnRequest(
                date.AddDays(1),
                "برگشت یک عدد",
                new[]
                {
                    new SalesReturnLineRequest(
                        originalLine.Id,
                        1m)
                }));

        Assert.Equal(22_000m, salesReturn.GrandTotal);
        Assert.Equal(12_000m, salesReturn.CostTotal);

        var stock = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(4m, stock.Quantity);

        var journal = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.Id ==
                    salesReturn.AccountingJournalEntryId);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        var refreshedInvoice =
            (await store.GetSalesInvoicesAsync(
                fixture.Company.Id))
            .Single(x => x.Id == invoice.Id);

        Assert.Equal(
            SalesInvoiceStatus.Posted,
            refreshedInvoice.Status);
    }

    [Fact]
    public async Task LotAndSerialTracking_AndLowStockAlerts_AreEnforced()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);
        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);
        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "SER-001",
                "کالای سریالی تست",
                "626000000999",
                "عدد",
                ProductKind.Inventory,
                true,
                30_000m,
                20_000m,
                InventoryTrackingMode.Serial,
                1m));

        var date = fiscalYear.StartDate.AddDays(10);

        var purchase = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseReceiptRequest(
                fiscalYear.Id,
                date,
                warehouse.Id,
                null,
                PurchasePaymentType.Cash,
                null,
                new[]
                {
                    new PurchaseReceiptLineRequest(
                        product.Id,
                        1m,
                        20_000m,
                        0m,
                        0m,
                        null,
                        "SN-0001")
                }));

        await store.PostPurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            purchase.Id);

        var trace = await store.GetStockTraceBalancesAsync(
            fixture.Company.Id,
            warehouse.Id,
            product.Id);

        Assert.Single(trace);
        Assert.Equal("SN-0001", trace[0].SerialNumber);
        Assert.Equal(1m, trace[0].Quantity);

        var low = await store.GetLowStockAlertsAsync(
            fixture.Company.Id);

        Assert.Contains(
            low,
            x =>
                x.ProductId == product.Id &&
                x.WarehouseId == warehouse.Id);

        var found = await store.FindProductAsync(
            fixture.Company.Id,
            "626000000999");

        Assert.NotNull(found);
        Assert.Equal(product.Id, found!.Id);
    }

    [Fact]
    public async Task ForeignCurrencyJournal_PreservesOriginalAmount_AndRevaluesPosition()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var currencyService = new CurrencyAccountingService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await currencyService.EnsureDefaultsAsync(
            fixture.Company.Id);

        var currencies = await currencyService.GetCurrenciesAsync(
            fixture.Company.Id);

        var baseCurrency = currencies.Single(x => x.IsBase);
        var usd = currencies.Single(x => x.Code == "USD");

        Assert.Equal("IRR", baseCurrency.Code);

        var cash = await fixture.Db.Accounts.SingleAsync(
            x =>
                x.CompanyId == fixture.Company.Id &&
                x.Code == "1110");

        var revenue = await fixture.Db.Accounts.SingleAsync(
            x =>
                x.CompanyId == fixture.Company.Id &&
                x.Code == "4100");

        var documentDate = fiscalYear.StartDate.AddDays(10);

        await currencyService.SaveRateAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateCurrencyRateRequest(
                usd.Id,
                documentDate,
                590_000m,
                610_000m,
                600_000m,
                "Test"));

        var posted =
            await currencyService.CreateForeignCurrencyJournalAsync(
                fixture.Company.Id,
                fixture.User.Id,
                new CreateForeignCurrencyJournalRequest(
                    usd.Id,
                    documentDate,
                    "فروش ارزی تست",
                    new[]
                    {
                        new CreateForeignCurrencyJournalLineRequest(
                            cash.Id,
                            "دریافت دلار",
                            100m,
                            0m),
                        new CreateForeignCurrencyJournalLineRequest(
                            revenue.Id,
                            "درآمد ارزی",
                            0m,
                            100m)
                    },
                    fiscalYear.Id));

        Assert.Equal(100m, posted.ForeignDebitTotal);
        Assert.Equal(100m, posted.ForeignCreditTotal);
        Assert.Equal(60_000_000m, posted.BaseDebitTotal);
        Assert.Equal(60_000_000m, posted.BaseCreditTotal);
        Assert.Equal(600_000m, posted.ExchangeRate);

        var journal = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == posted.JournalEntryId);

        Assert.Equal(2, journal.Lines.Count);

        var journalLineIds = journal.Lines
            .Select(line => line.Id)
            .ToArray();

        var currencyRows = await fixture.Db.JournalLineCurrencies
            .AsNoTracking()
            .Where(x => journalLineIds.Contains(x.JournalLineId))
            .ToArrayAsync();

        Assert.Equal(2, currencyRows.Length);
        Assert.All(
            currencyRows,
            row =>
            {
                Assert.Equal(usd.Id, row.CurrencyId);
                Assert.Equal(600_000m, row.ExchangeRate);
            });

        var initialPosition =
            await currencyService.GetCurrencyPositionAsync(
                fixture.Company.Id,
                documentDate);

        var cashPosition = Assert.Single(
            initialPosition.Rows.Where(
                x =>
                    x.CurrencyCode == "USD" &&
                    x.AccountId == cash.Id));

        Assert.Equal(100m, cashPosition.ForeignBalance);
        Assert.Equal(
            60_000_000m,
            cashPosition.HistoricalBaseBalance);
        Assert.Equal(
            60_000_000m,
            cashPosition.RevaluedBaseBalance);
        Assert.Equal(0m, cashPosition.UnrealizedDifference);

        var revaluationDate = documentDate.AddDays(1);

        await currencyService.SaveRateAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateCurrencyRateRequest(
                usd.Id,
                revaluationDate,
                640_000m,
                660_000m,
                650_000m,
                "Test"));

        var revalued =
            await currencyService.GetCurrencyPositionAsync(
                fixture.Company.Id,
                revaluationDate);

        cashPosition = Assert.Single(
            revalued.Rows.Where(
                x =>
                    x.CurrencyCode == "USD" &&
                    x.AccountId == cash.Id));

        Assert.Equal(650_000m, cashPosition.CurrentRate);
        Assert.Equal(
            65_000_000m,
            cashPosition.RevaluedBaseBalance);
        Assert.Equal(
            5_000_000m,
            cashPosition.UnrealizedDifference);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => currencyService.SetBaseCurrencyAsync(
                fixture.Company.Id,
                fixture.User.Id,
                usd.Id));

        await accounting.ReverseJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            posted.JournalEntryId,
            new ReverseJournalRequest(
                revaluationDate,
                "برگشت تست ارزی",
                fiscalYear.Id));

        var afterReversal =
            await currencyService.GetCurrencyPositionAsync(
                fixture.Company.Id,
                revaluationDate);

        var reversedCashPosition =
            afterReversal.Rows.SingleOrDefault(
                x =>
                    x.CurrencyCode == "USD" &&
                    x.AccountId == cash.Id);

        Assert.NotNull(reversedCashPosition);
        Assert.Equal(
            0m,
            reversedCashPosition!.ForeignBalance);
        Assert.Equal(
            0m,
            reversedCashPosition.HistoricalBaseBalance);
        Assert.Equal(
            0m,
            reversedCashPosition.RevaluedBaseBalance);
    }

    [Fact]
    public async Task CurrencyRevaluation_PostsBaseAdjustment_WithoutChangingForeignBalance()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var currencies = new CurrencyAccountingService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);

        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);

        await currencies.EnsureDefaultsAsync(
            fixture.Company.Id);

        var usd = (await currencies.GetCurrenciesAsync(
            fixture.Company.Id))
            .Single(x => x.Code == "USD");

        var cash = await fixture.Db.Accounts.SingleAsync(
            x =>
                x.CompanyId == fixture.Company.Id &&
                x.Code == "1110");

        var gain = await fixture.Db.Accounts.SingleAsync(
            x =>
                x.CompanyId == fixture.Company.Id &&
                x.Code == "4100");

        var loss = await fixture.Db.Accounts.SingleAsync(
            x =>
                x.CompanyId == fixture.Company.Id &&
                x.Code == "5200");

        var documentDate = fiscalYear.StartDate.AddDays(30);
        var revaluationDate = documentDate.AddDays(1);

        await currencies.SaveRateAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateCurrencyRateRequest(
                usd.Id,
                documentDate,
                590_000m,
                610_000m,
                600_000m,
                "Test"));

        await currencies.CreateForeignCurrencyJournalAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateForeignCurrencyJournalRequest(
                usd.Id,
                documentDate,
                "دریافت ارزی",
                new[]
                {
                    new CreateForeignCurrencyJournalLineRequest(
                        cash.Id,
                        "دریافت دلار",
                        100m,
                        0m),
                    new CreateForeignCurrencyJournalLineRequest(
                        gain.Id,
                        "درآمد",
                        0m,
                        100m)
                },
                fiscalYear.Id));

        await currencies.SaveRateAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateCurrencyRateRequest(
                usd.Id,
                revaluationDate,
                640_000m,
                660_000m,
                650_000m,
                "Test"));

        var before = await currencies.GetCurrencyPositionAsync(
            fixture.Company.Id,
            revaluationDate);

        var beforeCash = before.Rows.Single(
            x =>
                x.CurrencyId == usd.Id &&
                x.AccountId == cash.Id);

        Assert.Equal(100m, beforeCash.ForeignBalance);
        Assert.Equal(60_000_000m, beforeCash.HistoricalBaseBalance);
        Assert.Equal(65_000_000m, beforeCash.RevaluedBaseBalance);
        Assert.Equal(5_000_000m, beforeCash.UnrealizedDifference);

        var posted = await currencies.PostRevaluationAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CurrencyRevaluationRequest(
                revaluationDate,
                gain.Id,
                loss.Id,
                fiscalYear.Id,
                "تسعیر پایان روز"));

        Assert.False(posted.NoAdjustmentRequired);
        Assert.NotNull(posted.JournalEntryId);
        Assert.NotNull(posted.JournalNumber);
        Assert.Equal(1, posted.PositionCount);
        Assert.Equal(5_000_000m, posted.GainTotal);
        Assert.Equal(0m, posted.LossTotal);

        var journal = await fixture.Db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == posted.JournalEntryId);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == cash.Id &&
                x.Debit == 5_000_000m);

        Assert.Contains(
            journal.Lines,
            x =>
                x.AccountId == gain.Id &&
                x.Credit == 5_000_000m);

        var cashLineId = journal.Lines
            .Single(x => x.AccountId == cash.Id)
            .Id;

        var currencyAdjustment =
            await fixture.Db.JournalLineCurrencies
                .AsNoTracking()
                .SingleAsync(x => x.JournalLineId == cashLineId);

        Assert.Equal(usd.Id, currencyAdjustment.CurrencyId);
        Assert.Equal(0m, currencyAdjustment.ForeignDebit);
        Assert.Equal(0m, currencyAdjustment.ForeignCredit);
        Assert.Equal(650_000m, currencyAdjustment.ExchangeRate);

        var after = await currencies.GetCurrencyPositionAsync(
            fixture.Company.Id,
            revaluationDate);

        var afterCash = after.Rows.Single(
            x =>
                x.CurrencyId == usd.Id &&
                x.AccountId == cash.Id);

        Assert.Equal(100m, afterCash.ForeignBalance);
        Assert.Equal(65_000_000m, afterCash.HistoricalBaseBalance);
        Assert.Equal(65_000_000m, afterCash.RevaluedBaseBalance);
        Assert.Equal(0m, afterCash.UnrealizedDifference);

        var repeated = await currencies.PostRevaluationAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CurrencyRevaluationRequest(
                revaluationDate,
                gain.Id,
                loss.Id,
                fiscalYear.Id));

        Assert.True(repeated.NoAdjustmentRequired);
        Assert.Null(repeated.JournalEntryId);
    }

    [Fact]
    public async Task ForeignTrade_PurchaseAndSale_KeepInventoryAtHistoricalBaseCost()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var accounting = new AccountingService(fixture.Db);
        var currencies = new CurrencyAccountingService(
            fixture.Db,
            accounting);
        var store = new SalesInventoryService(
            fixture.Db,
            accounting);

        await accounting.SeedDefaultAccountsAsync(
            fixture.Company.Id);
        var fiscalYear =
            await accounting.EnsureDefaultFiscalYearAsync(
                fixture.Company.Id);
        await currencies.EnsureDefaultsAsync(
            fixture.Company.Id);
        await store.EnsureDefaultsAsync(
            fixture.Company.Id);

        var usd = (await currencies.GetCurrenciesAsync(
            fixture.Company.Id))
            .Single(x => x.Code == "USD");

        var supplier = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "SUP-FX",
                "تامین‌کننده خارجی",
                DetailAccountType.Supplier,
                null));

        var customer = await accounting.CreateDetailAccountAsync(
            fixture.Company.Id,
            new CreateDetailAccountRequest(
                "CUS-FX",
                "مشتری خارجی",
                DetailAccountType.Customer,
                null));

        var warehouse = (await store.GetWarehousesAsync(
            fixture.Company.Id)).Single();

        var product = await store.CreateProductAsync(
            fixture.Company.Id,
            new CreateProductRequest(
                "FX-001",
                "کالای تجارت خارجی",
                null,
                "عدد",
                ProductKind.Inventory,
                true,
                0m,
                0m));

        var purchaseDate = fiscalYear.StartDate.AddDays(20);
        var saleDate = purchaseDate.AddDays(1);

        await currencies.SaveRateAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateCurrencyRateRequest(
                usd.Id,
                purchaseDate,
                590_000m,
                610_000m,
                600_000m,
                "Test"));

        var purchase = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreatePurchaseReceiptRequest(
                fiscalYear.Id,
                purchaseDate,
                warehouse.Id,
                supplier.Id,
                PurchasePaymentType.Credit,
                "خرید خارجی",
                new[]
                {
                    new PurchaseReceiptLineRequest(
                        product.Id,
                        10m,
                        10m,
                        0m,
                        0m)
                },
                usd.Id,
                null));

        Assert.Equal("USD", purchase.CurrencyCode);
        Assert.Equal(600_000m, purchase.ExchangeRate);
        Assert.Equal(100m, purchase.GrandTotal);

        await store.PostPurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            purchase.Id);

        var stockAfterPurchase = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(10m, stockAfterPurchase.Quantity);
        Assert.Equal(6_000_000m, stockAfterPurchase.AverageCost);
        Assert.Equal(60_000_000m, stockAfterPurchase.InventoryValue);

        await currencies.SaveRateAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateCurrencyRateRequest(
                usd.Id,
                saleDate,
                640_000m,
                660_000m,
                650_000m,
                "Test"));

        var sale = await store.CreateSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            new CreateSalesInvoiceRequest(
                fiscalYear.Id,
                saleDate,
                warehouse.Id,
                customer.Id,
                SalesPaymentType.Credit,
                "فروش خارجی",
                new[]
                {
                    new SalesInvoiceLineRequest(
                        product.Id,
                        2m,
                        15m,
                        0m,
                        0m)
                },
                usd.Id,
                null));

        Assert.Equal("USD", sale.CurrencyCode);
        Assert.Equal(650_000m, sale.ExchangeRate);
        Assert.Equal(30m, sale.GrandTotal);

        var postedSale = await store.PostSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            sale.Id);

        Assert.Equal(12_000_000m, postedSale.CostTotal);

        var stockAfterSale = (await store.GetStockBalancesAsync(
            fixture.Company.Id,
            warehouse.Id))
            .Single(x => x.ProductId == product.Id);

        Assert.Equal(8m, stockAfterSale.Quantity);
        Assert.Equal(6_000_000m, stockAfterSale.AverageCost);
        Assert.Equal(48_000_000m, stockAfterSale.InventoryValue);

        var settings = await fixture.Db.SalesInventorySettings
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == fixture.Company.Id);

        var position = await currencies.GetCurrencyPositionAsync(
            fixture.Company.Id,
            saleDate);

        var receivable = position.Rows.Single(
            x =>
                x.CurrencyId == usd.Id &&
                x.AccountId == settings.ReceivablesAccountId);

        Assert.Equal(30m, receivable.ForeignBalance);
        Assert.Equal(19_500_000m, receivable.HistoricalBaseBalance);
        Assert.Equal(19_500_000m, receivable.RevaluedBaseBalance);

        var payableAccountId = settings.PayablesAccountId!.Value;
        var payable = position.Rows.Single(
            x =>
                x.CurrencyId == usd.Id &&
                x.AccountId == payableAccountId);

        Assert.Equal(-100m, payable.ForeignBalance);
        Assert.Equal(-60_000_000m, payable.HistoricalBaseBalance);
        Assert.Equal(-65_000_000m, payable.RevaluedBaseBalance);
        Assert.Equal(-5_000_000m, payable.UnrealizedDifference);
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
