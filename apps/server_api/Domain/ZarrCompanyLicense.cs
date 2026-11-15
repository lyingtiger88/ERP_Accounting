namespace ERPAccounting.Api.Domain;

/// <summary>
/// Owner-verified mapping between a Zarr company and a Kaliia Supabase user.
/// A company subscription is shared across client devices, never per-install.
/// </summary>
public sealed class ZarrCompanyLicense
{
    public Guid CompanyId { get; set; }
    public Guid AuthUserId { get; set; }
    public Guid LinkedByUserId { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}
