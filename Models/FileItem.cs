namespace SshStudio.Models;

public sealed record FileItem(string Name, string Kind, string Size, string Modified)
{
    public string Type => Kind;
}
