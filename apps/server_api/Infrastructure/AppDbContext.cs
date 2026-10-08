using ERPAccounting.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TrustedDevice> Devices => Set<TrustedDevice>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<SecurityAuditLog> SecurityAuditLogs => Set<SecurityAuditLog>();
    public DbSet<LedgerAccount> Accounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<FiscalYearClosing> FiscalYearClosings => Set<FiscalYearClosing>();
    public DbSet<DetailAccount> DetailAccounts => Set<DetailAccount>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<AccountingProject> AccountingProjects => Set<AccountingProject>();
    public DbSet<JournalEntryFiscalYear> JournalEntryFiscalYears => Set<JournalEntryFiscalYear>();
    public DbSet<JournalLineDimension> JournalLineDimensions => Set<JournalLineDimension>();
    public DbSet<JournalSyncReceipt> JournalSyncReceipts => Set<JournalSyncReceipt>();
    public DbSet<JournalServerChange> JournalServerChanges => Set<JournalServerChange>();
    public DbSet<DetailAccountSyncState> DetailAccountSyncStates => Set<DetailAccountSyncState>();
    public DbSet<DetailAccountSyncReceipt> DetailAccountSyncReceipts => Set<DetailAccountSyncReceipt>();
    public DbSet<DetailAccountServerChange> DetailAccountServerChanges => Set<DetailAccountServerChange>();
    public DbSet<JournalReversalLink> JournalReversalLinks => Set<JournalReversalLink>();
    public DbSet<AccountingAuditLog> AccountingAuditLogs => Set<AccountingAuditLog>();
    public DbSet<CurrencyDefinition> Currencies => Set<CurrencyDefinition>();
    public DbSet<CurrencyExchangeRate> CurrencyExchangeRates => Set<CurrencyExchangeRate>();
    public DbSet<JournalLineCurrency> JournalLineCurrencies => Set<JournalLineCurrency>();
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<SalesInventorySettings> SalesInventorySettings => Set<SalesInventorySettings>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptLine> PurchaseReceiptLines => Set<PurchaseReceiptLine>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
    public DbSet<WarehouseTransfer> WarehouseTransfers => Set<WarehouseTransfer>();
    public DbSet<WarehouseTransferLine> WarehouseTransferLines => Set<WarehouseTransferLine>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<TreasuryAccount> TreasuryAccounts => Set<TreasuryAccount>();
    public DbSet<TreasuryTransaction> TreasuryTransactions => Set<TreasuryTransaction>();

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

        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.ToTable("auth_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccessTokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RefreshTokenHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RevokeReason).HasMaxLength(250);
            entity.HasIndex(x => x.AccessTokenHash).IsUnique();
            entity.HasIndex(x => x.RefreshTokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.RevokedAt });
            entity.HasIndex(x => x.RefreshExpiresAt);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TrustedDevice>()
                .WithMany()
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SecurityAuditLog>(entity =>
        {
            entity.ToTable("security_audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(120);
            entity.Property(x => x.Details).HasMaxLength(1000);
            entity.HasIndex(x => new { x.CompanyId, x.CreatedAt });
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<FiscalPeriod>(entity =>
        {
            entity.ToTable("fiscal_periods");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => new { x.FiscalYearId, x.PeriodNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.StartDate, x.EndDate });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FiscalYear>()
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FiscalYearClosing>(entity =>
        {
            entity.ToTable("fiscal_year_closings");
            entity.HasKey(x => x.FiscalYearId);
            entity.Property(x => x.NetResult).HasPrecision(20, 4);
            entity.HasIndex(x => x.ClosingJournalEntryId).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FiscalYear>()
                .WithOne()
                .HasForeignKey<FiscalYearClosing>(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<FiscalYearClosing>(x => x.ClosingJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.RetainedEarningsAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CostCenter>(entity =>
        {
            entity.ToTable("cost_centers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountingProject>(entity =>
        {
            entity.ToTable("accounting_projects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
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
            entity.HasIndex(x => x.CostCenterId);
            entity.HasIndex(x => x.ProjectId);
            entity.HasOne<JournalLine>()
                .WithOne()
                .HasForeignKey<JournalLineDimension>(x => x.JournalLineId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DetailAccount>()
                .WithMany()
                .HasForeignKey(x => x.DetailAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CostCenter>()
                .WithMany()
                .HasForeignKey(x => x.CostCenterId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AccountingProject>()
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
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

        modelBuilder.Entity<CurrencyDefinition>(entity =>
        {
            entity.ToTable("currencies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(12).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Symbol).HasMaxLength(20);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.IsBase });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CurrencyExchangeRate>(entity =>
        {
            entity.ToTable("currency_exchange_rates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(120).IsRequired();
            entity.Property(x => x.BuyRate).HasPrecision(24, 8);
            entity.Property(x => x.SellRate).HasPrecision(24, 8);
            entity.Property(x => x.AccountingRate).HasPrecision(24, 8);
            entity.HasIndex(x => new { x.CompanyId, x.CurrencyId, x.RateDate });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CurrencyDefinition>()
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLineCurrency>(entity =>
        {
            entity.ToTable("journal_line_currencies");
            entity.HasKey(x => x.JournalLineId);
            entity.Property(x => x.ForeignDebit).HasPrecision(24, 8);
            entity.Property(x => x.ForeignCredit).HasPrecision(24, 8);
            entity.Property(x => x.ExchangeRate).HasPrecision(24, 8);
            entity.HasIndex(x => x.CurrencyId);
            entity.HasOne<JournalLine>()
                .WithOne()
                .HasForeignKey<JournalLineCurrency>(x => x.JournalLineId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CurrencyDefinition>()
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoreProduct>(entity =>
        {
            entity.ToTable("store_products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Barcode).HasMaxLength(120);
            entity.Property(x => x.UnitName).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.TrackingMode).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.MinimumStock).HasPrecision(20, 4);
            entity.Property(x => x.SalesPrice).HasPrecision(20, 4);
            entity.Property(x => x.DefaultPurchasePrice).HasPrecision(20, 4);
            entity.HasIndex(x => new { x.CompanyId, x.Sku }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Barcode });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("warehouses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesInventorySettings>(entity =>
        {
            entity.ToTable("sales_inventory_settings");
            entity.HasKey(x => x.CompanyId);
            entity.HasOne<Company>()
                .WithOne()
                .HasForeignKey<SalesInventorySettings>(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.ReceivablesAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.PayablesAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.CashAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.SalesRevenueAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.InventoryAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.CostOfGoodsSoldAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.SalesTaxPayableAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(x => x.PurchaseTaxReceivableAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.ToTable("sales_invoices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PaymentType).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Subtotal).HasPrecision(20, 4);
            entity.Property(x => x.DiscountTotal).HasPrecision(20, 4);
            entity.Property(x => x.TaxTotal).HasPrecision(20, 4);
            entity.Property(x => x.GrandTotal).HasPrecision(20, 4);
            entity.Property(x => x.CostTotal).HasPrecision(20, 4);
            entity.Property(x => x.ExchangeRate).HasPrecision(24, 8);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CurrencyDefinition>()
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FiscalYear>()
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DetailAccount>()
                .WithMany()
                .HasForeignKey(x => x.CustomerDetailAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(x => x.AccountingJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(x => x.ReversalJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(x => x.SalesInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesInvoiceLine>(entity =>
        {
            entity.ToTable("sales_invoice_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(20, 4);
            entity.Property(x => x.DiscountAmount).HasPrecision(20, 4);
            entity.Property(x => x.TaxAmount).HasPrecision(20, 4);
            entity.Property(x => x.NetAmount).HasPrecision(20, 4);
            entity.Property(x => x.UnitCost).HasPrecision(20, 4);
            entity.Property(x => x.CostAmount).HasPrecision(20, 4);
            entity.Property(x => x.LotNumber).HasMaxLength(120);
            entity.Property(x => x.SerialNumber).HasMaxLength(180);
            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => x.SerialNumber);
            entity.HasOne<StoreProduct>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.ToTable("purchase_orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Subtotal).HasPrecision(20, 4);
            entity.Property(x => x.DiscountTotal).HasPrecision(20, 4);
            entity.Property(x => x.TaxTotal).HasPrecision(20, 4);
            entity.Property(x => x.GrandTotal).HasPrecision(20, 4);
            entity.Property(x => x.ExchangeRate).HasPrecision(24, 8);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CurrencyDefinition>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DetailAccount>().WithMany().HasForeignKey(x => x.SupplierDetailAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrderLine>(entity =>
        {
            entity.ToTable("purchase_order_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.ReceivedQuantity).HasPrecision(20, 4);
            entity.Property(x => x.UnitCost).HasPrecision(20, 4);
            entity.Property(x => x.DiscountAmount).HasPrecision(20, 4);
            entity.Property(x => x.TaxAmount).HasPrecision(20, 4);
            entity.Property(x => x.NetAmount).HasPrecision(20, 4);
            entity.HasIndex(x => x.ProductId);
            entity.HasOne<StoreProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseReceipt>(entity =>
        {
            entity.ToTable("purchase_receipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PaymentType).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Subtotal).HasPrecision(20, 4);
            entity.Property(x => x.DiscountTotal).HasPrecision(20, 4);
            entity.Property(x => x.TaxTotal).HasPrecision(20, 4);
            entity.Property(x => x.GrandTotal).HasPrecision(20, 4);
            entity.Property(x => x.ExchangeRate).HasPrecision(24, 8);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CurrencyDefinition>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DetailAccount>().WithMany().HasForeignKey(x => x.SupplierDetailAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.AccountingJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseReceiptLine>(entity =>
        {
            entity.ToTable("purchase_receipt_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.UnitCost).HasPrecision(20, 4);
            entity.Property(x => x.DiscountAmount).HasPrecision(20, 4);
            entity.Property(x => x.TaxAmount).HasPrecision(20, 4);
            entity.Property(x => x.NetAmount).HasPrecision(20, 4);
            entity.Property(x => x.LotNumber).HasMaxLength(120);
            entity.Property(x => x.SerialNumber).HasMaxLength(180);
            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => x.SerialNumber);
            entity.HasIndex(x => x.PurchaseOrderLineId);
            entity.HasOne<StoreProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseReturn>(entity =>
        {
            entity.ToTable("purchase_returns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.GrandTotal).HasPrecision(20, 4);
            entity.Property(x => x.TaxTotal).HasPrecision(20, 4);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PurchaseReceipt>().WithMany().HasForeignKey(x => x.PurchaseReceiptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.AccountingJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseReturnLine>(entity =>
        {
            entity.ToTable("purchase_return_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.NetAmount).HasPrecision(20, 4);
            entity.Property(x => x.TaxAmount).HasPrecision(20, 4);
            entity.Property(x => x.UnitCost).HasPrecision(20, 4);
            entity.HasIndex(x => x.PurchaseReceiptLineId);
            entity.HasIndex(x => x.ProductId);
            entity.HasOne<PurchaseReceiptLine>().WithMany().HasForeignKey(x => x.PurchaseReceiptLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoreProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WarehouseTransfer>(entity =>
        {
            entity.ToTable("warehouse_transfers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.WarehouseTransferId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WarehouseTransferLine>(entity =>
        {
            entity.ToTable("warehouse_transfer_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.LotNumber).HasMaxLength(120);
            entity.Property(x => x.SerialNumber).HasMaxLength(180);
            entity.HasIndex(x => x.ProductId);
            entity.HasOne<StoreProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesReturn>(entity =>
        {
            entity.ToTable("sales_returns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.GrandTotal).HasPrecision(20, 4);
            entity.Property(x => x.TaxTotal).HasPrecision(20, 4);
            entity.Property(x => x.CostTotal).HasPrecision(20, 4);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.AccountingJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesReturnLine>(entity =>
        {
            entity.ToTable("sales_return_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.NetAmount).HasPrecision(20, 4);
            entity.Property(x => x.TaxAmount).HasPrecision(20, 4);
            entity.Property(x => x.UnitCost).HasPrecision(20, 4);
            entity.Property(x => x.CostAmount).HasPrecision(20, 4);
            entity.HasIndex(x => x.SalesInvoiceLineId);
            entity.HasIndex(x => x.ProductId);
            entity.HasOne<SalesInvoiceLine>().WithMany().HasForeignKey(x => x.SalesInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoreProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.UnitCost).HasPrecision(20, 4);
            entity.Property(x => x.LotNumber).HasMaxLength(120);
            entity.Property(x => x.SerialNumber).HasMaxLength(180);
            entity.Property(x => x.ReferenceType).HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.ProductId, x.DocumentDate });
            entity.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
            entity.HasIndex(x => new { x.CompanyId, x.ProductId, x.LotNumber });
            entity.HasIndex(x => new { x.CompanyId, x.SerialNumber });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoreProduct>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TreasuryAccount>(entity =>
        {
            entity.ToTable("treasury_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CurrencyDefinition>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TreasuryTransaction>(entity =>
        {
            entity.ToTable("treasury_transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ExchangeRate).HasPrecision(24, 8);
            entity.Property(x => x.Amount).HasPrecision(20, 4);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DocumentDate });
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TreasuryAccount>().WithMany().HasForeignKey(x => x.FromTreasuryAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TreasuryAccount>().WithMany().HasForeignKey(x => x.ToTreasuryAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.CounterAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DetailAccount>().WithMany().HasForeignKey(x => x.DetailAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CurrencyDefinition>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.AccountingJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
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
