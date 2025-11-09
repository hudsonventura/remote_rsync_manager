using System.Collections.Concurrent;
using server.Models;

namespace server.Services;

public class TokenStore : ITokenStore
{
    private static readonly ConcurrentDictionary<string, StoredToken> _tokens = new();

    public void StoreToken(string token, DateTime expiresAt, string email)
    {
        var storedToken = new StoredToken
        {
            Token = token,
            ExpiresAt = expiresAt,
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        _tokens.AddOrUpdate(token, storedToken, (key, oldValue) => storedToken);
    }

    public bool IsTokenValid(string token)
    {
        if (!_tokens.TryGetValue(token, out var storedToken))
            return false;

        if (storedToken.ExpiresAt <= DateTime.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            return false;
        }

        return true;
    }

    public void RemoveToken(string token)
    {
        _tokens.TryRemove(token, out _);
    }

    public void RemoveExpiredTokens()
    {
        var expiredTokens = _tokens
            .Where(kvp => kvp.Value.ExpiresAt <= DateTime.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in expiredTokens)
        {
            _tokens.TryRemove(token, out _);
        }
    }

    public List<StoredToken> GetAllTokens()
    {
        return _tokens.Values.ToList();
    }
}

