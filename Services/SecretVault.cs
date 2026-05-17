using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SshStudio.Models;

namespace SshStudio.Services;

public sealed class SecretVault
{
    private const string Prefix = "enc:v1:";
    private const string LocalPrefix = "dpapi:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] VerifierPlaintext = Encoding.UTF8.GetBytes("SshStudio vault verifier");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _metadataPath;
    private VaultMetadata? _metadata;
    private byte[]? _key;

    public SecretVault(string root)
    {
        _metadataPath = Path.Combine(root, "vault.json");
    }

    public bool Exists => File.Exists(_metadataPath);
    public bool IsUnlocked => _key is not null;

    public bool Unlock(string masterPassword)
    {
        if (string.IsNullOrEmpty(masterPassword))
        {
            return false;
        }

        if (!Exists)
        {
            Create(masterPassword);
            return true;
        }

        var metadata = LoadMetadata();
        var key = DeriveKey(masterPassword, Convert.FromBase64String(metadata.Salt), metadata.Iterations);
        try
        {
            DecryptValue(metadata.Verifier, key);
            _metadata = metadata;
            _key = key;
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(key);
            return false;
        }
        catch (FormatException)
        {
            CryptographicOperations.ZeroMemory(key);
            return false;
        }
    }

    public string Encrypt(string value)
    {
        if (string.IsNullOrEmpty(value) || IsEncrypted(value) || IsLocallyEncrypted(value))
        {
            return value;
        }

        return EncryptForCurrentUser(value);
    }

    public string Decrypt(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (IsLocallyEncrypted(value))
        {
            return DecryptForCurrentUser(value);
        }

        if (IsEncrypted(value))
        {
            EnsureUnlocked();
            return DecryptValue(value, _key!);
        }

        return value;
    }

    public static bool IsEncrypted(string value)
    {
        return value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static bool IsLocallyEncrypted(string value)
    {
        return value.StartsWith(LocalPrefix, StringComparison.Ordinal);
    }

    private void Create(string masterPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var metadata = new VaultMetadata
        {
            Salt = Convert.ToBase64String(salt)
        };
        var key = DeriveKey(masterPassword, salt, metadata.Iterations);
        metadata.Verifier = EncryptValue(Encoding.UTF8.GetString(VerifierPlaintext), key);
        File.WriteAllText(_metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        _metadata = metadata;
        _key = key;
    }

    private VaultMetadata LoadMetadata()
    {
        return JsonSerializer.Deserialize<VaultMetadata>(File.ReadAllText(_metadataPath), JsonOptions)
            ?? throw new InvalidOperationException("主密码配置损坏。");
    }

    private void EnsureUnlocked()
    {
        if (_key is null || _metadata is null)
        {
            throw new InvalidOperationException("请先输入主密码解锁。");
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    private static string EncryptValue(string value, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);
        CryptographicOperations.ZeroMemory(plaintext);
        return Prefix + Convert.ToBase64String(payload);
    }

    private static string DecryptValue(string value, byte[] key)
    {
        var payload = Convert.FromBase64String(value[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("密文格式无效。");
        }

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var ciphertext = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static string EncryptForCurrentUser(string value)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plaintext);
        return LocalPrefix + Convert.ToBase64String(protectedBytes);
    }

    private static string DecryptForCurrentUser(string value)
    {
        var protectedBytes = Convert.FromBase64String(value[LocalPrefix.Length..]);
        var plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }
}
