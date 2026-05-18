using CommunityToolkit.Mvvm.ComponentModel;

namespace SshStudio.Models;

public sealed partial class HostProfile : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string address = "";
    [ObservableProperty] private int port = 22;
    [ObservableProperty] private string user = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string privateKey = "";
    [ObservableProperty] private string defaultPath = "/root";
    [ObservableProperty] private string hostKeyFingerprint = "";
    [ObservableProperty] private string hostKeyAlgorithm = "";
    [ObservableProperty] private string status = "未连接";
    [ObservableProperty] private double cpu;
    [ObservableProperty] private double memory;
    [ObservableProperty] private string bandwidth = "--";

    public string DisplayAddress => $"{User}@{Address}:{Port}";
}
