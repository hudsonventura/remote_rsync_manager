using Microsoft.AspNetCore.DataProtection;

namespace server.Services;

public class SecretProtectionService : ISecretProtectionService
{
    private const string EncryptedPrefix = "ENC:";
    private readonly IDataProtector _protector;
    private readonly ILogger<SecretProtectionService> _logger;

    public SecretProtectionService(IDataProtectionProvider provider, ILogger<SecretProtectionService> logger)
    {
        _protector = provider.CreateProtector("Remember.Secrets.v1");
        _logger = logger;
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return plaintext;
        }

        // If already protected, don't re-protect
        if (plaintext.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return plaintext;
        }

        try
        {
            return EncryptedPrefix + _protector.Protect(plaintext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to protect sensitive secret data");
            return plaintext;
        }
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            return ciphertext;
        }

        // If not protected with our prefix, return as-is (e.g. legacy plaintext SSH key or token)
        if (!ciphertext.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return ciphertext;
        }

        try
        {
            var actualCipher = ciphertext.Substring(EncryptedPrefix.Length);
            return _protector.Unprotect(actualCipher);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect encrypted secret data; returning raw value");
            return ciphertext;
        }
    }
}
