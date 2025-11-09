using server.Models;

namespace server.Services;

public interface IAuthService
{
    Task<User?> ValidateUserAsync(string email, string password);
    Task<User?> GetUserByEmailAsync(string email);
}

