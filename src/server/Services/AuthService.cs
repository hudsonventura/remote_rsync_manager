using System.Security.Cryptography;
using System.Text;
using server.Models;

namespace server.Services;

public class AuthService : IAuthService
{
    // In-memory user store for demo purposes
    // In production, replace this with a database
    private readonly List<User> _users = new()
    {
        new User
        {
            Id = 1,
            Email = "user@example.com",
            PasswordHash = HashPassword("password123") // password: password123
        }
    };

    public Task<User?> ValidateUserAsync(string email, string password)
    {
        var user = _users.FirstOrDefault(u => u.Email == email);
        
        if (user == null)
            return Task.FromResult<User?>(null);

        var passwordHash = HashPassword(password);
        if (user.PasswordHash != passwordHash)
            return Task.FromResult<User?>(null);

        return Task.FromResult<User?>(user);
    }

    public Task<User?> GetUserByEmailAsync(string email)
    {
        var user = _users.FirstOrDefault(u => u.Email == email);
        return Task.FromResult<User?>(user);
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}

