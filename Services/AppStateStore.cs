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

    public bool VerifyMasterPassword(string masterPassword)
    {
        return _vault.Unlock(masterPassword);
    }

    public List<HostProfile> LoadHosts()
    {
        var hosts = Load<List<HostProfile>>("hosts.json") ?? [];
        foreach (var host in hosts)
        {
            host.Password = _vault.Decrypt(host.Password);
            host.PrivateKey = _vault.Decrypt(host.PrivateKey);
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
            Status = host.Status,
            Cpu = host.Cpu,
            Memory = host.Memory,
            Bandwidth = host.Bandwidth
        }).ToList();
        Save("hosts.json", persisted);
    }

    public ApiConfig LoadApiConfig()
    {
        var config = Load<ApiConfig>("api-config.json") ?? new ApiConfig();
        config.ApiKey = _vault.Decrypt(config.ApiKey);
        return config;
    }

    public void SaveApiConfig(ApiConfig config)
    {
        Save("api-config.json", new ApiConfig
        {
            BaseUrl = config.BaseUrl,
            ApiKey = _vault.Encrypt(config.ApiKey),
            Model = config.Model,
            ExecutionMode = config.ExecutionMode,
            EnableWebSearch = config.EnableWebSearch,
            SystemPrompt = config.SystemPrompt
        });
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
}
