using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class AccountingSyncValidationTests
{
    [Fact]
    public async Task SyncJournal_RejectsMissingIdempotencyIdentifiersBeforeDatabaseWork()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        var service = new AccountingService(db);

        var missingChangeId = new SyncJournalRequest(
            " ", "local-1", null, new DateOnly(2026, 1, 1), null, []);

        var missingLocalId = new SyncJournalRequest(
            "change-1", " ", null, new DateOnly(2026, 1, 1), null, []);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SyncJournalAsync(Guid.NewGuid(), Guid.NewGuid(), missingChangeId));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SyncJournalAsync(Guid.NewGuid(), Guid.NewGuid(), missingLocalId));
    }

    [Fact]
    public async Task SyncJournal_RejectsOversizedOrControlCharacterIdentifiers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        var service = new AccountingService(db);

        var oversized = new SyncJournalRequest(
            new string('x', 129), "local-1", null,
            new DateOnly(2026, 1, 1), null, []);
        var controlCharacter = new SyncJournalRequest(
            "change-1", "local-1\nforged", null,
            new DateOnly(2026, 1, 1), null, []);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SyncJournalAsync(Guid.NewGuid(), Guid.NewGuid(), oversized));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SyncJournalAsync(Guid.NewGuid(), Guid.NewGuid(), controlCharacter));
    }
}
