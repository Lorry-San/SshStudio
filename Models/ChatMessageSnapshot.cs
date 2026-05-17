namespace SshStudio.Models;

public sealed class ChatMessageSnapshot
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string Tone { get; set; } = "normal";
}
