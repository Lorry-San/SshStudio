using CommunityToolkit.Mvvm.ComponentModel;

namespace SshStudio.Models;

public sealed partial class ChatMessage : ObservableObject
{
    public ChatMessage(string role, string text, string tone)
    {
        Role = role;
        Text = text;
        Tone = tone;
    }

    [ObservableProperty] private string role = "";
    [ObservableProperty] private string text = "";
    [ObservableProperty] private string tone = "normal";
}
