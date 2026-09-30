using ERPAccounting.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TrustedDevice> Devices => Set<TrustedDevice>();
    public DbSet<LedgerAccount> Accounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<DetailAccount> DetailAccounts => Set<DetailAccount>();
    public DbSet<JournalEntryFiscalYear> JournalEntryFiscalYears => Set<JournalEntryFiscalYear>();
    public DbSet<JournalLineDimension> JournalLineDimensions => Set<JournalLineDimension>();
    public DbSet<JournalSyncReceipt> JournalSyncReceipts => Set<JournalSyncReceipt>();
    public DbSet<JournalServerChange> JournalServerChanges => Set<JournalServerChange>();
    public DbSet<DetailAccountSyncState> DetailAccountSyncStates => Set<DetailAccountSyncState>();
    public DbSet<DetailAccountSyncReceipt> DetailAccountSyncReceipts => Set<DetailAccountSyncReceipt>();
    public DbSet<DetailAccountServerChange> DetailAccountServerChanges => Set<DetailAccountServerChange>();
    public DbSet<JournalReversalLink> JournalReversalLinks => Set<JournalReversalLink>();
    public DbSet<AccountingAuditLog> AccountingAuditLogs => Set<AccountingAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LegalName).HasMaxLength(250);
            entity.Property(x => x.TaxId).HasMaxLength(80);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasIndex(x => x.CompanyId);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrustedDevice>(entity =>
        {
            entity.ToTable("trusted_devices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PublicKeyFingerprint).HasMaxLength(256);
            entity.Property(x => x.TrustState).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.UserId, x.TrustState });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LedgerAccount>(entity =>
        {
            entity.ToTable("ledger_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.ToTable("journal_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalLine>(entity =>
        {
            entity.ToTable("journal_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Debit).HasPrecision(20, 4);
            entity.Property(x => x.Credit).HasPrecision(20, 4);
            entity.HasIndex(x => x.AccountId);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FiscalYear>(entity =>
        {
            entity.ToTable("fiscal_years");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.StartDate, x.EndDate });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DetailAccount>(entity =>
        {
            entity.ToTable("detail_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.NationalId).HasMaxLength(50);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Name });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalEntryFiscalYear>(entity =>
        {
            entity.ToTable("journal_entry_fiscal_years");
            entity.HasKey(x => x.JournalEntryId);
            entity.HasIndex(x => x.FiscalYearId);
            entity.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<JournalEntryFiscalYear>(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FiscalYear>()
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLineDimension>(entity =>
        {
            entity.ToTable("journal_line_dimensions");
            entity.HasKey(x => x.JournalLineId);
            entity.HasIndex(x => x.DetailAccountId);
            entity.HasOne<JournalLine>()
                .WithOne()
                .HasForeignKey<JournalLineDimension>(x => x.JournalLineId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DetailAccount>()
                .WithMany()
                .HasForeignKey(x => x.DetailAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalSyncReceipt>(entity =>
        {
            entity.ToTable("journal_sync_receipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ChangeId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LocalDocumentId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.ChangeId }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.LocalDocumentId }).IsUnique();
            entity.HasIndex(x => x.JournalEntryId).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<JournalSyncReceipt>(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalServerChange>(entity =>
        {
            entity.ToTable("journal_server_changes");
            entity.HasKey(x => x.Sequence);
            entity.Property(x => x.Sequence).ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.CompanyId, x.Sequence });
            entity.HasIndex(x => x.JournalEntryId).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<JournalServerChange>(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DetailAccountSyncState>(entity =>
        {
            entity.ToTable("detail_account_sync_states");
            entity.HasKey(x => x.DetailAccountId);
            entity.HasIndex(x => new { x.CompanyId, x.Revision });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DetailAccount>()
                .WithOne()
                .HasForeignKey<DetailAccountSyncState>(x => x.DetailAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DetailAccountSyncReceipt>(entity =>
        {
            entity.ToTable("detail_account_sync_receipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ChangeId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.ChangeId }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DetailAccountId, x.AppliedRevision });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DetailAccount>()
                .WithMany()
                .HasForeignKey(x => x.DetailAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DetailAccountServerChange>(entity =>
        {
            entity.ToTable("detail_account_server_changes");
            entity.HasKey(x => x.Sequence);
            entity.Property(x => x.Sequence).ValueGeneratedOnAdd();
            entity.Property(x => x.Operation).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.Sequence });
            entity.HasIndex(x => new { x.DetailAccountId, x.Revision }).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DetailAccount>()
                .WithMany()
                .HasForeignKey(x => x.DetailAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalReversalLink>(entity =>
        {
            entity.ToTable("journal_reversal_links");
            entity.HasKey(x => x.OriginalJournalEntryId);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => x.ReversalJournalEntryId).IsUnique();
            entity.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<JournalReversalLink>(x => x.OriginalJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<JournalReversalLink>(x => x.ReversalJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingAuditLog>(entity =>
        {
            entity.ToTable("accounting_audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.PayloadJson);
            entity.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityId });
            entity.HasIndex(x => new { x.CompanyId, x.CreatedAt });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
