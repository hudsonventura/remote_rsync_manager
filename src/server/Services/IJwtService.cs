using server.Models;

namespace server.Services;

public interface IJwtService
{
    string GenerateToken(User user);
}

