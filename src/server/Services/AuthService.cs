using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Services;

public class AuthService : IAuthService
{
    private readonly DBContext _dbContext;

    public AuthService(DBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> ValidateUserAsync(string username, string password)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => (u.username == username || u.email == username) && u.isActive);
        
        if (user == null)
            return null;

        var passwordHash = HashPassword(password);
        if (user.passwordHash != passwordHash)
            return null;

        return user;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _dbContext.Users
            .FirstOrDefaultAsync(u => u.email == email && u.isActive);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _dbContext.Users
            .FirstOrDefaultAsync(u => u.username == username && u.isActive);
    }

    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}

