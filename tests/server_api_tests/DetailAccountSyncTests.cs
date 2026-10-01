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
            firstDate,
            firstDate);

        Assert.Single(ledger);
        Assert.Equal(1_000m, ledger[0].Debit);
        Assert.Equal(1_000m, ledger[0].RunningBalance);

        var trial = await service.GetTrialBalanceAsync(
            fixture.Company.Id,
            firstDate,
            firstDate);

        Assert.Equal(
            1_000m,
            trial.Single(x => x.AccountId == cash.Id).DebitTurnover);
        Assert.Equal(
            1_000m,
            trial.Single(x => x.AccountId == revenue.Id).CreditTurnover);
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
