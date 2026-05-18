namespace SshStudio.Models;

public sealed class VaultMetadata
{
    public string Version { get; set; } = "1";
    public string Kdf { get; set; } = "PBKDF2-SHA256";
    public int Iterations { get; set; } = 210_000;
    public string Salt { get; set; } = "";
    public string Verifier { get; set; } = "";
    public string LocalUnlockKey { get; set; } = "";
    public bool RequireMasterPasswordOnStartup { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
