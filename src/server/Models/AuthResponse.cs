namespace server.Models;

public record AuthResponse(string Token, string Email, DateTime ExpiresAt);

