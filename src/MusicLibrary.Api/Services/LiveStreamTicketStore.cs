using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MusicLibrary.Api.Services;

public sealed class LiveStreamTicketStore
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, LiveStreamTicketEntry> _tickets = new(StringComparer.Ordinal);

    public string Issue(Guid stationId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _tickets.Where(pair => pair.Value.ExpiresAt <= now))
        {
            _tickets.TryRemove(expired.Key, out _);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tickets[token] = new LiveStreamTicketEntry(stationId, now.Add(TicketLifetime));
        return token;
    }

    public bool TryResolve(string token, out Guid stationId)
    {
        stationId = default;
        if (!_tickets.TryGetValue(token, out var ticket)) return false;
        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _tickets.TryRemove(token, out _);
            return false;
        }

        stationId = ticket.StationId;
        return true;
    }

    private sealed record LiveStreamTicketEntry(Guid StationId, DateTimeOffset ExpiresAt);
}