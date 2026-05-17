namespace SshStudio.Models;

public sealed class ApiConfig
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5.4";
    public string ExecutionMode { get; set; } = "confirm";
    public bool EnableWebSearch { get; set; }
    public string SystemPrompt { get; set; } = "你是 SSH 运维助手。";
}
