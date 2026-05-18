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
    private const string VerifierText = "SshStudio vault verifier";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _metadataPath;
    private VaultMetadata? _metadata;
    private byte[]? _key;
    private bool _hasLegacyLocalSecrets;

    public SecretVault(string root)
    {
        _metadataPath = Path.Combine(root, "vault.json");
    }

    public bool Exists => File.Exists(_metadataPath);
    public bool IsUnlocked => _key is not null;
    public bool HasLegacyLocalSecrets => _hasLegacyLocalSecrets;
    public bool RequireMasterPasswordOnStartup => Exists && LoadMetadata().RequireMasterPasswordOnStartup;

    public bool TryAutoUnlock()
    {
        if (!Exists)
        {
            return false;
        }

        var metadata = LoadMetadata();
        if (metadata.RequireMasterPasswordOnStartup)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(metadata.LocalUnlockKey))
        {
            return false;
        }

        try
        {
            var key = Convert.FromBase64String(DecryptForCurrentUser(metadata.LocalUnlockKey));
            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                return false;
            }

            DecryptValue(metadata.Verifier, key);
            _metadata = metadata;
            _key = key;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

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
            EnsureLocalUnlockKey();
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            CryptographicOperations.ZeroMemory(key);
            return false;
        }
    }

    public string Encrypt(string value)
    {
        if (string.IsNullOrEmpty(value) || IsEncrypted(value))
        {
            return value;
        }

        EnsureUnlocked();

        if (IsLocallyEncrypted(value))
        {
            value = DecryptForCurrentUser(value);
        }

        return EncryptValue(value, _key!);
    }

    public string Decrypt(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (IsLocallyEncrypted(value))
        {
            _hasLegacyLocalSecrets = true;
            return DecryptForCurrentUser(value);
        }

        if (IsEncrypted(value))
        {
            EnsureUnlocked();
            return DecryptValue(value, _key!);
        }

        return value;
    }

    public bool SetRequireMasterPasswordOnStartup(bool require)
    {
        if (!Exists)
        {
            return false;
        }

        var metadata = _metadata ?? LoadMetadata();
        metadata.RequireMasterPasswordOnStartup = require;
        if (require)
        {
            metadata.LocalUnlockKey = "";
        }
        else if (IsUnlocked)
        {
            _metadata = metadata;
            EnsureLocalUnlockKey();
            return true;
        }

        SaveMetadata(metadata);
        _metadata = metadata;
        return true;
    }

    public bool ChangeMasterPassword(string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6 || !Unlock(currentPassword))
        {
            return false;
        }

        var metadata = _metadata ?? LoadMetadata();
        var oldKey = _key!;
        var salt = RandomNumberGenerator.GetBytes(16);
        var newKey = DeriveKey(newPassword, salt, metadata.Iterations);
        metadata.Salt = Convert.ToBase64String(salt);
        metadata.Verifier = EncryptValue(VerifierText, newKey);
        metadata.LocalUnlockKey = metadata.RequireMasterPasswordOnStartup
            ? ""
            : EncryptForCurrentUser(Convert.ToBase64String(newKey));

        _key = newKey;
        _metadata = metadata;
        SaveMetadata(metadata);
        CryptographicOperations.ZeroMemory(oldKey);
        return true;
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
        metadata.Verifier = EncryptValue(VerifierText, key);
        _metadata = metadata;
        _key = key;
        EnsureLocalUnlockKey();
    }

    private VaultMetadata LoadMetadata()
    {
        return JsonSerializer.Deserialize<VaultMetadata>(File.ReadAllText(_metadataPath), JsonOptions)
            ?? throw new InvalidOperationException("Vault metadata is corrupted.");
    }

    private void EnsureUnlocked()
    {
        if (_key is null || _metadata is null)
        {
            throw new InvalidOperationException("Please enter the master password first.");
        }
    }

    private void EnsureLocalUnlockKey()
    {
        EnsureUnlocked();
        if (CanUseLocalUnlockKey(_metadata!.LocalUnlockKey))
        {
            return;
        }

        _metadata.LocalUnlockKey = EncryptForCurrentUser(Convert.ToBase64String(_key!));
        SaveMetadata(_metadata);
    }

    private static bool CanUseLocalUnlockKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(DecryptForCurrentUser(value)).Length == KeySize;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private void SaveMetadata(VaultMetadata metadata)
    {
        File.WriteAllText(_metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
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
            throw new CryptographicException("Invalid ciphertext format.");
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
