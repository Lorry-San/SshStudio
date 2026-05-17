namespace SshStudio.Models;

public sealed class AiMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string HostKey { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
