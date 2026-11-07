using server.Models;

namespace server.Services;

public interface IAuthService
{
    Task<User?> ValidateUserAsync(string username, string password);
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByUsernameAsync(string username);
    string HashPassword(User user, string password);
    bool VerifyPassword(User user, string password);
}
