using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Services;

public class AuthService : IAuthService
{
    private readonly DBContext _dbContext;
    private readonly PasswordHasher<User> _passwordHasher = new();
    private readonly ILogger<AuthService> _logger;

    public AuthService(DBContext dbContext, ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<User?> ValidateUserAsync(string username, string password)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => (u.username == username || u.email == username) && u.isActive);

        if (user == null)
            return null;

        // 1. Try standard Identity password verification (PBKDF2)
        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.passwordHash, password);
        if (verificationResult == PasswordVerificationResult.Success)
        {
            return user;
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.passwordHash = _passwordHasher.HashPassword(user, password);
            user.updatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return user;
        }

        // 2. Check for legacy SHA-256 hash format (44 Base64 chars)
        if (IsLegacySha256Hash(user.passwordHash))
        {
            var legacyHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            var storedBytes = TryDecodeBase64(user.passwordHash);

            if (storedBytes != null && CryptographicOperations.FixedTimeEquals(legacyHashBytes, storedBytes))
            {
                // Transparent rehash to secure PBKDF2
                _logger.LogInformation("Upgrading password hash from legacy SHA-256 to PBKDF2 for user {Username}", user.username);
                user.passwordHash = _passwordHasher.HashPassword(user, password);
                if (string.IsNullOrEmpty(user.securityStamp))
                {
                    user.securityStamp = Guid.NewGuid().ToString();
                }
                user.updatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return user;
            }
        }

        return null;
    }

    public string HashPassword(User user, string password)
    {
        return _passwordHasher.HashPassword(user, password);
    }

    public static string HashPassword(string password)
    {
        var dummyUser = new User();
        var hasher = new PasswordHasher<User>();
        return hasher.HashPassword(dummyUser, password);
    }

    public bool VerifyPassword(User user, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(user, user.passwordHash, password);
        if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
            return true;

        if (IsLegacySha256Hash(user.passwordHash))
        {
            var legacyHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            var storedBytes = TryDecodeBase64(user.passwordHash);
            if (storedBytes != null && CryptographicOperations.FixedTimeEquals(legacyHashBytes, storedBytes))
                return true;
        }

        return false;
    }

    private static bool IsLegacySha256Hash(string hash)
    {
        return !string.IsNullOrEmpty(hash) && hash.Length == 44 && !hash.StartsWith("$AQAAAA");
    }

    private static byte[]? TryDecodeBase64(string s)
    {
        try { return Convert.FromBase64String(s); } catch { return null; }
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
}
