namespace SshStudio.Models;

public sealed class CommandReviewResult
{
    public bool Allow { get; set; }
    public string Risk { get; set; } = "danger";
    public string Reason { get; set; } = "";
}
