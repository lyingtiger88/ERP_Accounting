using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Infrastructure;

/// <summary>
/// Development-only storage used to make Phase 1 runnable before PostgreSQL is introduced.
/// Do not use this implementation in production.
/// </summary>
public sealed class InMemoryStore
{
    public object Gate { get; } = new();
    public Dictionary<Guid, Company> Companies { get; } = new();
    public Dictionary<Guid, AppUser> Users { get; } = new();
    public Dictionary<Guid, TrustedDevice> Devices { get; } = new();
}
