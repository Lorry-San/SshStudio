using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshStudio.Models;
using SshStudio.Services;

namespace SshStudio.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppStateStore _store;
    private readonly SshClientService _ssh = new();
    private readonly AiClientService _ai = new();
    private readonly StringBuilder _recentTerminal = new();
    private readonly StringBuilder _commandCapture = new();
    private readonly StringBuilder _pendingTerminalOutput = new();
    private readonly DispatcherTimer _metricsTimer = new();
    private readonly DispatcherTimer _terminalFlushTimer = new();
    private readonly object _terminalBufferSync = new();
    private readonly object _commandSync = new();
    private readonly List<HostChatHistory> _chatHistories;
    private TaskCompletionSource<string>? _commandCompletion;
    private CancellationTokenSource? _commandWaitCts;
    private string? _activeCommandMarker;
    private string _activeChatHostKey = "";
    private bool _loadingMessages;
    private bool _aiCommandRunning;
    private long _lastNetworkBytes;

    [ObservableProperty] private HostProfile? selectedHost;
    [ObservableProperty] private string remotePath = "/root";
    [ObservableProperty] private string terminalText = "SshStudio 已启动。\n请选择主机连接。\n";
    [ObservableProperty] private string shellInput = "";
    [ObservableProperty] private string aiInput = "";
    [ObservableProperty] private string pendingCommand = "";
    [ObservableProperty] private string statusText = "就绪";
    [ObservableProperty] private bool useMemory;
    [ObservableProperty] private bool isCommandRunning;
    [ObservableProperty] private ApiConfig apiConfig;

    public bool HasPendingCommand => !string.IsNullOrWhiteSpace(PendingCommand);
    public bool CanCancelCommand => IsCommandRunning;
    public bool RequireMasterPasswordOnStartup
    {
        get => _store.RequireMasterPasswordOnStartup;
        set
        {
            _store.SetRequireMasterPasswordOnStartup(value);
            OnPropertyChanged();
        }
    }

    public event Action<string>? TerminalOutputReceived;
    public event Action? TerminalResetRequested;

    public ObservableCollection<HostProfile> Hosts { get; } = [];
    public ObservableCollection<FileItem> Files { get; } = [];
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ObservableCollection<BandwidthSample> BandwidthSamples { get; } = [];
    public ObservableCollection<AiMemory> Memories { get; } = [];

    public MainWindowViewModel(AppStateStore? store = null)
    {
        _store = store ?? new AppStateStore();
        _ssh.ShellOutputReceived += OnShellOutputReceived;
        apiConfig = _store.LoadApiConfig();
        _chatHistories = _store.LoadChatHistories();

        foreach (var memory in _store.LoadMemories())
        {
            Memories.Add(memory);
        }

        foreach (var host in _store.LoadHosts())
        {
            Hosts.Add(host);
        }

        if (Hosts.Count == 0)
        {
            Hosts.Add(new HostProfile { Name = "示例主机", Address = "127.0.0.1", User = "root", Status = "未连接" });
        }

        for (var i = 0; i < 30; i++)
        {
            BandwidthSamples.Add(new BandwidthSample(6));
        }

        _metricsTimer.Interval = TimeSpan.FromSeconds(1);
        _metricsTimer.Tick += async (_, _) => await RefreshMetricsAsync();

        _terminalFlushTimer.Interval = TimeSpan.FromMilliseconds(120);
        _terminalFlushTimer.Tick += (_, _) => FlushTerminalOutput();
        _terminalFlushTimer.Start();

        SelectedHost = Hosts.FirstOrDefault();
        SwitchChatContext();
    }

    [RelayCommand]
    private async Task SendShellInputAsync()
    {
        if (string.IsNullOrEmpty(ShellInput))
        {
            return;
        }

        var input = ShellInput.Replace("\\n", "\n", StringComparison.Ordinal);
        ShellInput = "";
        await _ssh.WriteShellAsync(input);
    }

    [RelayCommand]
    private async Task ConnectSelectedAsync()
    {
        if (SelectedHost is null)
        {
            return;
        }

        try
        {
            StatusText = "正在连接...";
            TerminalText = "";
            TerminalResetRequested?.Invoke();
            _recentTerminal.Clear();
            _commandCapture.Clear();
            ClearPendingTerminalOutput();
            await _ssh.ConnectAsync(SelectedHost);
            SaveHosts();
            SelectedHost.Status = "已连接";
            RemotePath = string.IsNullOrWhiteSpace(SelectedHost.DefaultPath) ? "/root" : SelectedHost.DefaultPath;
            AppendTerminal("已连接 " + SelectedHost.DisplayAddress);
            if (!string.IsNullOrWhiteSpace(SelectedHost.HostKeyFingerprint))
            {
                AppendTerminal("[SSH] 主机指纹 " + SelectedHost.HostKeyFingerprint);
            }
            StatusText = "已连接";
            _lastNetworkBytes = 0;
            _metricsTimer.Start();
            await RefreshSftpAsync();
        }
        catch (Exception ex)
        {
            SelectedHost.Status = "连接失败";
            AppendTerminal("连接失败: " + ex.Message);
            StatusText = "连接失败";
        }

        OnPropertyChanged(nameof(Hosts));
    }

    [RelayCommand]
    private async Task ExecutePendingCommandAsync()
    {
        var command = PendingCommand.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        PendingCommand = "";
        await ExecuteCommandAsync(command, returnToAi: true);
    }

    [RelayCommand]
    private async Task RefreshSftpAsync()
    {
        try
        {
            Files.Clear();
            foreach (var item in await _ssh.ListFilesAsync(RemotePath))
            {
                Files.Add(item);
            }
            StatusText = "SFTP 已刷新";
        }
        catch (Exception ex)
        {
            StatusText = "SFTP 失败";
            AppendTerminal("SFTP 刷新失败: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task SendAiAsync()
    {
        var input = AiInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        AddMessage(new ChatMessage("user", input, "user"));
        AiInput = "";

        StatusText = "AI 流式分析中...";
        var assistantMessage = new ChatMessage("assistant", "正在分析...", "normal");
        AddMessage(assistantMessage);
        AiAction action;
        try
        {
            action = await _ai.RequestActionAsync(
                ApiConfig,
                input,
                BuildFullContext(),
                _ =>
                {
                    Dispatcher.UIThread.Post(() => StatusText = "AI 流式接收中...");
                    return Task.CompletedTask;
                });
        }
        catch (Exception ex)
        {
            assistantMessage.Text = "AI 请求失败: " + ex.Message;
            assistantMessage.Tone = "danger";
            SaveCurrentChatHistory();
            StatusText = "AI 失败";
            return;
        }

        assistantMessage.Text = action.Message;
        assistantMessage.Tone = action.Risk;
        SaveCurrentChatHistory();
        PendingCommand = action.Command;
        StatusText = "AI 完成";

        await TryAutoExecuteReviewedCommandAsync(action);
    }

    [RelayCommand]
    private void SaveMemory()
    {
        var title = SelectedHost?.DisplayAddress ?? "全局记忆";
        var content = AiInput.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        Memories.Add(new AiMemory
        {
            Title = title,
            Content = content,
            HostKey = GetHostKey(SelectedHost),
            UpdatedAt = DateTime.UtcNow
        });
        AiInput = "";
        SaveMemories();
    }

    [RelayCommand]
    private void ClearCurrentChat()
    {
        Messages.Clear();
        AddMessage(new ChatMessage("assistant", "当前主机聊天记录已清空。", "normal"));
        SaveCurrentChatHistory();
    }

    [RelayCommand]
    private void CancelRunningCommand()
    {
        _commandWaitCts?.Cancel();
        StatusText = "已停止等待命令";
        AddMessage(new ChatMessage("assistant", "已停止等待当前 AI 命令完成；远端命令可能仍在 PTY 中运行，如需停止请在终端发送 Ctrl+C。", "warning"));
    }

    [RelayCommand]
    private void ResetHostFingerprint()
    {
        if (SelectedHost is null)
        {
            return;
        }

        SelectedHost.HostKeyFingerprint = "";
        SelectedHost.HostKeyAlgorithm = "";
        SaveHosts();
        StatusText = "已重置主机指纹";
    }

    public void SaveHosts()
    {
        _store.SaveHosts(Hosts);
    }

    public void SaveApiConfig()
    {
        _store.SaveApiConfig(ApiConfig);
    }

    public bool VaultExists => _store.VaultExists;

    public bool VerifyMasterPassword(string masterPassword)
    {
        return _store.VerifyMasterPassword(masterPassword);
    }

    public bool ChangeMasterPassword(string currentPassword, string newPassword)
    {
        return _store.ChangeMasterPassword(currentPassword, newPassword);
    }

    public void ExportConfigPackage(string path)
    {
        _store.ExportConfigPackage(path);
        StatusText = "配置已导出";
    }

    public void ImportConfigPackage(string path)
    {
        _store.ImportConfigPackage(path);
        StatusText = "配置已导入，重启后生效";
    }

    public void AddOrUpdateHost(HostProfile host, HostProfile? existing)
    {
        if (existing is null)
        {
            Hosts.Add(host);
        }
        else
        {
            var index = Hosts.IndexOf(existing);
            if (index >= 0)
            {
                Hosts[index] = host;
            }
        }

        SelectedHost = host;
        SaveHosts();
    }

    public void DeleteHost(HostProfile host)
    {
        Hosts.Remove(host);
        SelectedHost = Hosts.FirstOrDefault();
        SaveHosts();
    }

    public void ClearPendingCommand()
    {
        PendingCommand = "";
    }

    public async Task ExecuteCommandAsync(string command, bool returnToAi)
    {
        _aiCommandRunning = true;
        IsCommandRunning = true;
        _commandWaitCts?.Dispose();
        _commandWaitCts = new CancellationTokenSource();
        try
        {
            var marker = "__SSHSTUDIO_DONE_" + Guid.NewGuid().ToString("N") + "__";
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_commandSync)
            {
                _activeCommandMarker = marker;
                _commandCompletion = completion;
            }

            _commandCapture.Clear();
            AppendTerminal("[AI/审核] " + command);
            StatusText = "命令执行中...";
            await _ssh.SendCommandToShellAsync(command);
            await _ssh.SendCommandToShellAsync($"printf '\\n{marker}:%s\\n' $?");

            var timeout = TimeSpan.FromSeconds(Math.Clamp(ApiConfig.CommandTimeoutSeconds, 15, 3600));
            var output = await WaitForCommandCompletionAsync(completion, timeout, _commandWaitCts.Token);
            StatusText = "命令完成";
            if (returnToAi && !_commandWaitCts.IsCancellationRequested)
            {
                _ = ContinueAiAfterCommandAsync(command, output);
            }
        }
        catch (Exception ex)
        {
            AppendTerminal("执行失败: " + ex.Message);
            StatusText = "执行失败";
        }
        finally
        {
            _aiCommandRunning = false;
            IsCommandRunning = false;
            lock (_commandSync)
            {
                _activeCommandMarker = null;
                _commandCompletion = null;
            }
        }
    }

    public async Task WritePtyAsync(string text)
    {
        try
        {
            await _ssh.WriteShellAsync(text);
        }
        catch (Exception ex)
        {
            AppendTerminal("\n[PTY 写入失败] " + ex.Message);
        }
    }

    public void ResizePty(int columns, int rows, int width, int height)
    {
        _ssh.ResizePty(columns, rows, width, height);
    }

    public async Task SendShellTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var input = text.Replace("\\n", "\n", StringComparison.Ordinal);
        if (!input.EndsWith('\n') && !input.EndsWith('\r'))
        {
            input += "\n";
        }
        await WritePtyAsync(input);
    }

    public async Task UploadSftpFileAsync(string localPath)
    {
        var fileName = Path.GetFileName(localPath);
        var remoteFile = CombineRemotePath(RemotePath, fileName);
        await _ssh.UploadFileAsync(localPath, remoteFile);
        StatusText = "上传完成";
        await RefreshSftpAsync();
    }

    public async Task<string> DownloadSftpFileAsync(FileItem item)
    {
        if (item.Kind == "目录")
        {
            RemotePath = CombineRemotePath(RemotePath, item.Name);
            await RefreshSftpAsync();
            return "";
        }

        var localDir = Path.Combine(Path.GetTempPath(), "SshStudio", "downloads");
        var localPath = Path.Combine(localDir, item.Name);
        await _ssh.DownloadFileAsync(CombineRemotePath(RemotePath, item.Name), localPath);
        StatusText = "下载完成";
        return localPath;
    }

    public async Task RenameSftpFileAsync(FileItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name)
        {
            return;
        }

        await _ssh.RenameFileAsync(CombineRemotePath(RemotePath, item.Name), CombineRemotePath(RemotePath, newName.Trim()));
        StatusText = "重命名完成";
        await RefreshSftpAsync();
    }

    private async Task RefreshMetricsAsync()
    {
        if (SelectedHost is null || _aiCommandRunning)
        {
            return;
        }

        try
        {
            var output = await _ssh.ExecuteAsync(
                "bash -lc \"printf 'CPU:%s\\n' $(awk '{print $1}' /proc/loadavg); " +
                "free | awk '/Mem:/ {printf \\\"MEM:%d\\\\n\\\", ($3/$2)*100}'; " +
                "awk -F'[: ]+' '$1 !~ /lo/ && $1 != \\\"\\\" {rx+=$3; tx+=$11} END {printf \\\"NET:%d\\\\n\\\", rx+tx}' /proc/net/dev\"");
            var netBytes = _lastNetworkBytes;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("CPU:", StringComparison.OrdinalIgnoreCase) && double.TryParse(line[4..], out var load))
                {
                    SelectedHost.Cpu = Math.Clamp(load * 20, 0, 100);
                }
                else if (line.StartsWith("MEM:", StringComparison.OrdinalIgnoreCase) && double.TryParse(line[4..], out var mem))
                {
                    SelectedHost.Memory = Math.Clamp(mem, 0, 100);
                }
                else if (line.StartsWith("NET:", StringComparison.OrdinalIgnoreCase) && long.TryParse(line[4..], out var bytes))
                {
                    netBytes = bytes;
                }
            }

            if (_lastNetworkBytes > 0 && netBytes >= _lastNetworkBytes)
            {
                var perSecond = netBytes - _lastNetworkBytes;
                SelectedHost.Bandwidth = FormatRate(perSecond);
                var height = Math.Log10(Math.Max(1, perSecond)) * 7;
                BandwidthSamples.RemoveAt(0);
                BandwidthSamples.Add(new BandwidthSample(height));
            }
            _lastNetworkBytes = netBytes;
        }
        catch
        {
            // Metrics are best-effort; do not disturb the terminal workflow.
        }
    }

    private async Task ContinueAiAfterCommandAsync(string command, string output)
    {
        var observation = "OBSERVATION\n已执行命令:\n" + command + "\n\n命令输出:\n" + output;
        try
        {
            var action = await _ai.RequestActionAsync(ApiConfig, observation, BuildFullContext());
            AddMessage(new ChatMessage("assistant", action.Message, action.Risk));
            PendingCommand = action.Command;
            await TryAutoExecuteReviewedCommandAsync(action);
        }
        catch (Exception ex)
        {
            AddMessage(new ChatMessage("assistant", "命令输出已捕获，但 AI 继续分析失败: " + ex.Message, "danger"));
        }
    }

    private async Task TryAutoExecuteReviewedCommandAsync(AiAction action)
    {
        if (action.Type != "command" ||
            ApiConfig.ExecutionMode != "auto_safe" ||
            string.IsNullOrWhiteSpace(action.Command))
        {
            return;
        }

        if (HasLocalHardBlock(action.Command))
        {
            AddMessage(new ChatMessage("assistant", "本地硬拦截：该命令包含多行、过长或明显危险结构，已转为人工审核。", "danger"));
            return;
        }

        CommandReviewResult review;
        try
        {
            StatusText = "AI 正在审核命令...";
            review = await _ai.ReviewCommandAsync(ApiConfig, action.Command, BuildFullContext());
        }
        catch (Exception ex)
        {
            AddMessage(new ChatMessage("assistant", "AI 命令审核失败，已转为人工审核: " + ex.Message, "warning"));
            StatusText = "AI 审核失败";
            return;
        }

        if (!review.Allow || review.Risk == "danger")
        {
            AddMessage(new ChatMessage("assistant", "AI 审核未放行，已转为人工审核: " + review.Reason, "warning"));
            StatusText = "AI 审核未放行";
            return;
        }

        AddMessage(new ChatMessage("assistant", "AI 审核通过，自动执行命令: " + review.Reason, review.Risk));
        PendingCommand = "";
        await ExecuteCommandAsync(action.Command, returnToAi: true);
    }

    private void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        SaveCurrentChatHistory();
    }

    private void SwitchChatContext()
    {
        SaveCurrentChatHistory();
        _activeChatHostKey = GetHostKey(SelectedHost);
        _loadingMessages = true;
        Messages.Clear();

        var history = GetHistory(_activeChatHostKey);
        foreach (var item in history.Messages)
        {
            Messages.Add(new ChatMessage(item.Role, item.Text, item.Tone));
        }

        if (Messages.Count == 0)
        {
            Messages.Add(new ChatMessage("assistant", "我会按当前主机隔离上下文。需要长期记忆时打开“加载记忆”。", "normal"));
        }

        _loadingMessages = false;
    }

    private void SaveCurrentChatHistory()
    {
        if (_loadingMessages || string.IsNullOrWhiteSpace(_activeChatHostKey))
        {
            return;
        }

        var history = GetHistory(_activeChatHostKey);
        history.Messages = Messages
            .TakeLast(40)
            .Select(message => new ChatMessageSnapshot
            {
                Role = message.Role,
                Text = message.Text,
                Tone = message.Tone
            })
            .ToList();
        _store.SaveChatHistories(_chatHistories);
    }

    private HostChatHistory GetHistory(string hostKey)
    {
        var history = _chatHistories.FirstOrDefault(item => item.HostKey == hostKey);
        if (history is not null)
        {
            return history;
        }

        history = new HostChatHistory { HostKey = hostKey };
        _chatHistories.Add(history);
        return history;
    }

    private string BuildFullContext()
    {
        var builder = new StringBuilder();
        builder.AppendLine("当前主机:");
        builder.AppendLine(SelectedHost is null ? "未选择主机" : SelectedHost.DisplayAddress);
        builder.AppendLine("当前路径: " + RemotePath);
        builder.AppendLine();

        if (UseMemory)
        {
            var memories = Memories
                .Where(memory => memory.Enabled && (string.IsNullOrWhiteSpace(memory.HostKey) || memory.HostKey == _activeChatHostKey))
                .TakeLast(12)
                .ToList();
            if (memories.Count > 0)
            {
                builder.AppendLine("已加载长期记忆:");
                foreach (var memory in memories)
                {
                    builder.AppendLine("- " + memory.Title + ": " + memory.Content);
                }
                builder.AppendLine();
            }
        }

        builder.AppendLine("最近聊天记录:");
        foreach (var message in Messages.TakeLast(14))
        {
            builder.AppendLine(message.Role + ": " + TrimForContext(message.Text, 1200));
        }
        builder.AppendLine();

        builder.AppendLine("当前终端最近输出:");
        builder.AppendLine(_recentTerminal.ToString());
        return builder.ToString();
    }

    private void SaveMemories()
    {
        _store.SaveMemories(Memories);
    }

    private void AppendTerminal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        QueueTerminalOutput(text + "\n");
    }

    private void OnShellOutputReceived(string text)
    {
        QueueTerminalOutput(SanitizeTerminalOutput(text));
    }

    private void QueueTerminalOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_terminalBufferSync)
        {
            _pendingTerminalOutput.Append(text);
        }
    }

    private void FlushTerminalOutput()
    {
        string text;
        lock (_terminalBufferSync)
        {
            if (_pendingTerminalOutput.Length == 0)
            {
                return;
            }
            text = _pendingTerminalOutput.ToString();
            _pendingTerminalOutput.Clear();
        }

        var commandCompleted = false;
        TaskCompletionSource<string>? completion = null;
        lock (_commandSync)
        {
            if (_activeCommandMarker is not null && text.Contains(_activeCommandMarker, StringComparison.Ordinal))
            {
                text = StripCommandMarker(text, _activeCommandMarker);
                commandCompleted = true;
                completion = _commandCompletion;
            }
        }

        TerminalOutputReceived?.Invoke(text);
        _recentTerminal.Append(text);
        _commandCapture.Append(text);
        TrimBuilder(_recentTerminal, 16000);
        TrimBuilder(_commandCapture, 12000);

        if (commandCompleted)
        {
            completion?.TrySetResult(_commandCapture.ToString());
        }
    }

    private void ClearPendingTerminalOutput()
    {
        lock (_terminalBufferSync)
        {
            _pendingTerminalOutput.Clear();
        }
    }

    private static string SanitizeTerminalOutput(string text)
    {
        return text
            .Replace("\u001b[?2004h", "", StringComparison.Ordinal)
            .Replace("\u001b[?2004l", "", StringComparison.Ordinal)
            .Replace("\u001b[?25h", "", StringComparison.Ordinal)
            .Replace("\u001b[?25l", "", StringComparison.Ordinal)
            .Replace("?2004h", "", StringComparison.Ordinal)
            .Replace("?2004l", "", StringComparison.Ordinal)
            .Replace("?25h", "", StringComparison.Ordinal)
            .Replace("?25l", "", StringComparison.Ordinal);
    }

    private async Task<string> WaitForCommandCompletionAsync(
        TaskCompletionSource<string> completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken));
        if (finished == completion.Task)
        {
            return await completion.Task;
        }

        var output = _commandCapture.ToString();
        var message = cancellationToken.IsCancellationRequested
            ? "已取消等待命令完成；远端命令可能仍在运行。"
            : "命令还没有返回完成标记，可能仍在执行；我先停止等待，请查看中间终端输出。";
        AddMessage(new ChatMessage("assistant", message, "warning"));
        return output;
    }

    private static string StripCommandMarker(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return text;
        }

        var lineStart = text.LastIndexOf('\n', index);
        lineStart = lineStart < 0 ? index : lineStart;
        var lineEnd = text.IndexOf('\n', index);
        if (lineEnd < 0)
        {
            return text[..lineStart];
        }

        return text[..lineStart] + text[(lineEnd + 1)..];
    }

    private static string GetHostKey(HostProfile? host)
    {
        return host is null ? "__no_host__" : $"{host.User}@{host.Address}:{host.Port}";
    }

    private static string CombineRemotePath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory == "/")
        {
            return "/" + name.TrimStart('/');
        }

        return directory.TrimEnd('/') + "/" + name.TrimStart('/');
    }

    private static bool HasLocalHardBlock(string command)
    {
        var normalized = command.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('\n') || normalized.Length > 600)
        {
            return true;
        }

        var lower = normalized.ToLowerInvariant();
        return lower == "rm" ||
               lower.StartsWith("rm ", StringComparison.Ordinal) ||
               lower.StartsWith("rm\t", StringComparison.Ordinal) ||
               lower.Contains("; rm ", StringComparison.Ordinal) ||
               lower.Contains("&& rm ", StringComparison.Ordinal) ||
               lower.Contains("|| rm ", StringComparison.Ordinal) ||
               lower.Contains("| rm ", StringComparison.Ordinal) ||
               lower == "rmdir" ||
               lower.StartsWith("rmdir ", StringComparison.Ordinal) ||
               lower.StartsWith("rmdir\t", StringComparison.Ordinal);
    }

    private static string FormatRate(long bytesPerSecond)
    {
        var value = (double)bytesPerSecond;
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static void TrimBuilder(StringBuilder builder, int maxLength)
    {
        if (builder.Length > maxLength)
        {
            builder.Remove(0, builder.Length - maxLength);
        }
    }

    private static string TrimForContext(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    partial void OnSelectedHostChanged(HostProfile? value)
    {
        SwitchChatContext();
    }

    partial void OnPendingCommandChanged(string value)
    {
        OnPropertyChanged(nameof(HasPendingCommand));
    }

    partial void OnIsCommandRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelCommand));
    }
}
