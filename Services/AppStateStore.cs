using System.Text.Json;
using SshStudio.Models;

namespace SshStudio.Services;

public sealed class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _root;
    private readonly SecretVault _vault;

    public AppStateStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SshStudio");
        Directory.CreateDirectory(_root);
        _vault = new SecretVault(_root);
    }

    public bool VaultExists => _vault.Exists;
    public bool IsVaultUnlocked => _vault.IsUnlocked;
    public bool RequireMasterPasswordOnStartup => _vault.RequireMasterPasswordOnStartup;

    public bool TryAutoUnlock()
    {
        return _vault.TryAutoUnlock();
    }

    public bool VerifyMasterPassword(string masterPassword)
    {
        return _vault.Unlock(masterPassword);
    }

    public bool SetRequireMasterPasswordOnStartup(bool require)
    {
        return _vault.SetRequireMasterPasswordOnStartup(require);
    }

    public bool ChangeMasterPassword(string currentPassword, string newPassword)
    {
        var hosts = LoadHosts();
        var apiConfig = LoadApiConfig();
        if (!_vault.ChangeMasterPassword(currentPassword, newPassword))
        {
            return false;
        }

        SaveHosts(hosts);
        SaveApiConfig(apiConfig);
        return true;
    }

    public List<HostProfile> LoadHosts()
    {
        var hosts = Load<List<HostProfile>>("hosts.json") ?? [];
        var shouldMigrateSecrets = false;
        foreach (var host in hosts)
        {
            shouldMigrateSecrets |= ShouldMigrateSecret(host.Password);
            shouldMigrateSecrets |= ShouldMigrateSecret(host.PrivateKey);
            host.Password = _vault.Decrypt(host.Password);
            host.PrivateKey = _vault.Decrypt(host.PrivateKey);
            host.Status = "未连接";
            host.Cpu = 0;
            host.Memory = 0;
            host.Bandwidth = "--";
        }

        if (shouldMigrateSecrets || _vault.HasLegacyLocalSecrets)
        {
            SaveHosts(hosts);
        }

        return hosts;
    }

    public void SaveHosts(IEnumerable<HostProfile> hosts)
    {
        var persisted = hosts.Select(host => new HostProfile
        {
            Name = host.Name,
            Address = host.Address,
            Port = host.Port,
            User = host.User,
            Password = _vault.Encrypt(host.Password),
            PrivateKey = _vault.Encrypt(host.PrivateKey),
            DefaultPath = host.DefaultPath,
            HostKeyFingerprint = host.HostKeyFingerprint,
            HostKeyAlgorithm = host.HostKeyAlgorithm,
            Status = "未连接",
            Cpu = 0,
            Memory = 0,
            Bandwidth = "--"
        }).ToList();
        Save("hosts.json", persisted);
    }

    public ApiConfig LoadApiConfig()
    {
        var config = Load<ApiConfig>("api-config.json") ?? new ApiConfig();
        var shouldMigrateSecrets = ShouldMigrateSecret(config.ApiKey);
        config.ApiKey = _vault.Decrypt(config.ApiKey);
        if (shouldMigrateSecrets || _vault.HasLegacyLocalSecrets)
        {
            SaveApiConfig(config);
        }

        return config;
    }

    public void SaveApiConfig(ApiConfig config)
    {
        Save("api-config.json", new ApiConfig
        {
            BaseUrl = config.BaseUrl,
            ApiKey = _vault.Encrypt(config.ApiKey),
            Model = config.Model,
            ApiMode = config.ApiMode,
            ExecutionMode = config.ExecutionMode,
            EnableWebSearch = config.EnableWebSearch,
            CommandTimeoutSeconds = config.CommandTimeoutSeconds,
            SystemPrompt = config.SystemPrompt,
            ReviewPrompt = config.ReviewPrompt
        });
    }

    public void ExportConfigPackage(string path)
    {
        var package = new Dictionary<string, JsonElement>();
        AddFileIfExists(package, "vault", "vault.json");
        AddFileIfExists(package, "hosts", "hosts.json");
        AddFileIfExists(package, "apiConfig", "api-config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(package, JsonOptions));
    }

    public void ImportConfigPackage(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        WriteFileIfPresent(root, "vault", "vault.json");
        WriteFileIfPresent(root, "hosts", "hosts.json");
        WriteFileIfPresent(root, "apiConfig", "api-config.json");
    }

    public List<HostChatHistory> LoadChatHistories()
    {
        return Load<List<HostChatHistory>>("chat-histories.json") ?? [];
    }

    public void SaveChatHistories(IEnumerable<HostChatHistory> histories)
    {
        Save("chat-histories.json", histories);
    }

    public List<AiMemory> LoadMemories()
    {
        return Load<List<AiMemory>>("memories.json") ?? [];
    }

    public void SaveMemories(IEnumerable<AiMemory> memories)
    {
        Save("memories.json", memories);
    }

    private T? Load<T>(string name)
    {
        var path = Path.Combine(_root, name);
        if (!File.Exists(path))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
    }

    private void Save<T>(string name, T value)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private void AddFileIfExists(Dictionary<string, JsonElement> package, string key, string name)
    {
        var path = Path.Combine(_root, name);
        if (!File.Exists(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        package[key] = document.RootElement.Clone();
    }

    private void WriteFileIfPresent(JsonElement root, string key, string name)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return;
        }

        Save(name, value);
    }

    private static bool ShouldMigrateSecret(string value)
    {
        return !string.IsNullOrEmpty(value) && !SecretVault.IsEncrypted(value);
    }
}
