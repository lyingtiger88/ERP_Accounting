using System.Collections.Concurrent;

namespace ERPAccounting.Api.Security;

public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions =
        new(StringComparer.Ordinal);

    public void Add(string token, Guid userId, DateTimeOffset expiresAt)
        => _sessions[token] = new SessionRecord(userId, expiresAt);

    public bool TryResolve(string token, out Guid userId)
    {
        userId = Guid.Empty;

        if (!_sessions.TryGetValue(token, out var session))
        {
            return false;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        userId = session.UserId;
        return true;
    }

    private sealed record SessionRecord(Guid UserId, DateTimeOffset ExpiresAt);
}
