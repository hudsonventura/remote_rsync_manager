using server.Models;

namespace server.Services;

public interface ITokenStore
{
    void StoreToken(string token, DateTime expiresAt, string email);
    bool IsTokenValid(string token);
    void RemoveToken(string token);
    void RemoveExpiredTokens();
    List<StoredToken> GetAllTokens();
}

