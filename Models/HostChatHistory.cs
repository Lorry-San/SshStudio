namespace SshStudio.Models;

public sealed class HostChatHistory
{
    public string HostKey { get; set; } = "";
    public List<ChatMessageSnapshot> Messages { get; set; } = [];
}
