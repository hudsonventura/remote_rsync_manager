namespace server.Services;

public interface ISecretProtectionService
{
    string? Protect(string? plaintext);
    string? Unprotect(string? ciphertext);
}
