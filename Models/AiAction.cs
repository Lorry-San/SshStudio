namespace SshStudio.Models;

public sealed class AiAction
{
    public string Type { get; set; } = "final";
    public string Message { get; set; } = "";
    public string Intent { get; set; } = "";
    public string Command { get; set; } = "";
    public string Risk { get; set; } = "safe";
    public bool Wait { get; set; } = true;
    public bool ReturnAfter { get; set; } = true;
    public string Next { get; set; } = "";
}
