using System.Reflection;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshStudio.Models;

namespace SshStudio.Services;

public sealed class SshClientService : IDisposable
{
    private SshClient? _ssh;
    private SftpClient? _sftp;
    private ShellStream? _shell;
    private CancellationTokenSource? _shellReaderCts;
    private int _ptyColumns = 120;
    private int _ptyRows = 24;
    private int _ptyWidth = 1200;
    private int _ptyHeight = 480;

    public bool IsConnected => _ssh?.IsConnected == true;
    public event Action<string>? ShellOutputReceived;

    public Task ConnectAsync(HostProfile host)
    {
        return Task.Run(() =>
        {
            Disconnect();
            var connection = BuildConnectionInfo(host);
            _ssh = new SshClient(connection);
            _sftp = new SftpClient(connection);
            string? hostKeyError = null;
            var learnedHostKey = false;

            void VerifyHostKey(object? _, HostKeyEventArgs args)
            {
                var fingerprint = NormalizeSha256Fingerprint(args.FingerPrintSHA256);
                if (string.IsNullOrWhiteSpace(fingerprint))
                {
                    args.CanTrust = false;
                    hostKeyError = "SSH host key fingerprint is empty.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(host.HostKeyFingerprint))
                {
                    host.HostKeyFingerprint = fingerprint;
                    host.HostKeyAlgorithm = args.HostKeyName;
                    learnedHostKey = true;
                    args.CanTrust = true;
                    return;
                }

                if (string.Equals(host.HostKeyFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    args.CanTrust = true;
                    return;
                }

                args.CanTrust = false;
                hostKeyError = "SSH host key changed. Expected " + host.HostKeyFingerprint + ", got " + fingerprint + ".";
            }

            _ssh.HostKeyReceived += VerifyHostKey;
            _sftp.HostKeyReceived += VerifyHostKey;
            _ssh.Connect();
            if (!string.IsNullOrWhiteSpace(hostKeyError))
            {
                throw new SshConnectionException(hostKeyError);
            }

            _sftp.Connect();
            if (!string.IsNullOrWhiteSpace(hostKeyError))
            {
                throw new SshConnectionException(hostKeyError);
            }

            if (learnedHostKey)
            {
                ShellOutputReceived?.Invoke("\n[SSH] 已记录主机指纹 " + host.HostKeyFingerprint + "\n");
            }

            StartShellReader();
        });
    }

    public Task<string> ExecuteAsync(string command)
    {
        return Task.Run(() =>
        {
            if (_ssh is null || !_ssh.IsConnected)
            {
                throw new InvalidOperationException("SSH 未连接。");
            }

            using var cmd = _ssh.CreateCommand(command);
            var result = cmd.Execute();
            var error = cmd.Error;
            return string.IsNullOrWhiteSpace(error) ? result : result + "\n" + error;
        });
    }

    public Task WriteShellAsync(string input)
    {
        return Task.Run(() =>
        {
            if (_shell is null || !_shell.CanWrite)
            {
                throw new InvalidOperationException("PTY 未就绪。");
            }
            _shell.Write(input);
            _shell.Flush();
        });
    }

    public Task SendCommandToShellAsync(string command)
    {
        return WriteShellAsync(command.EndsWith('\n') ? command : command + "\n");
    }

    public void ResizePty(int columns, int rows, int width, int height)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        _ptyColumns = columns;
        _ptyRows = rows;
        _ptyWidth = Math.Max(width, columns * 8);
        _ptyHeight = Math.Max(height, rows * 16);

        if (_shell is null)
        {
            return;
        }

        TrySendWindowChange(_shell, _ptyColumns, _ptyRows, _ptyWidth, _ptyHeight);
    }

    public Task<IReadOnlyList<FileItem>> ListFilesAsync(string path)
    {
        return Task.Run<IReadOnlyList<FileItem>>(() =>
        {
            if (_sftp is null || !_sftp.IsConnected)
            {
                throw new InvalidOperationException("SFTP 未连接。");
            }

            return _sftp.ListDirectory(path)
                .Where(item => item.Name is not "." and not "..")
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name)
                .Select(item => new FileItem(
                    item.Name,
                    item.IsDirectory ? "目录" : "文件",
                    item.IsDirectory ? "--" : FormatBytes((ulong)item.Length),
                    item.LastWriteTime.ToString("yyyy-MM-dd HH:mm")))
                .ToList();
        });
    }

    public Task UploadFileAsync(string localPath, string remotePath)
    {
        return Task.Run(() =>
        {
            if (_sftp is null || !_sftp.IsConnected)
            {
                throw new InvalidOperationException("SFTP 未连接。");
            }

            using var stream = File.OpenRead(localPath);
            _sftp.UploadFile(stream, remotePath, canOverride: true);
        });
    }

    public Task DownloadFileAsync(string remotePath, string localPath)
    {
        return Task.Run(() =>
        {
            if (_sftp is null || !_sftp.IsConnected)
            {
                throw new InvalidOperationException("SFTP 未连接。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            using var stream = File.Create(localPath);
            _sftp.DownloadFile(remotePath, stream);
        });
    }

    public Task RenameFileAsync(string oldPath, string newPath)
    {
        return Task.Run(() =>
        {
            if (_sftp is null || !_sftp.IsConnected)
            {
                throw new InvalidOperationException("SFTP 未连接。");
            }

            _sftp.RenameFile(oldPath, newPath);
        });
    }

    public void Disconnect()
    {
        _shellReaderCts?.Cancel();
        _shell?.Dispose();
        _shellReaderCts?.Dispose();
        _shell = null;
        _shellReaderCts = null;
        if (_ssh?.IsConnected == true)
        {
            _ssh.Disconnect();
        }
        if (_sftp?.IsConnected == true)
        {
            _sftp.Disconnect();
        }
        _ssh?.Dispose();
        _sftp?.Dispose();
        _ssh = null;
        _sftp = null;
    }

    public void Dispose()
    {
        Disconnect();
    }

    private static ConnectionInfo BuildConnectionInfo(HostProfile host)
    {
        if (!string.IsNullOrWhiteSpace(host.PrivateKey))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(host.PrivateKey));
            var key = string.IsNullOrEmpty(host.Password)
                ? new PrivateKeyFile(stream)
                : new PrivateKeyFile(stream, host.Password);
            return new ConnectionInfo(host.Address, host.Port, host.User, new PrivateKeyAuthenticationMethod(host.User, key));
        }

        return new ConnectionInfo(
            host.Address,
            host.Port,
            host.User,
            new PasswordAuthenticationMethod(host.User, host.Password));
    }

    private void StartShellReader()
    {
        if (_ssh is null || !_ssh.IsConnected)
        {
            return;
        }

        _shell = _ssh.CreateShellStream("xterm-256color", (uint)_ptyColumns, (uint)_ptyRows, (uint)_ptyWidth, (uint)_ptyHeight, 4096);
        _shellReaderCts = new CancellationTokenSource();
        var token = _shellReaderCts.Token;
        _ = Task.Run(async () =>
        {
            var buffer = new byte[8192];
            while (!token.IsCancellationRequested && _shell is not null)
            {
                try
                {
                    if (_shell.DataAvailable)
                    {
                        var read = _shell.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            ShellOutputReceived?.Invoke(Encoding.UTF8.GetString(buffer, 0, read));
                        }
                    }
                    else
                    {
                        await Task.Delay(35, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ShellOutputReceived?.Invoke("\n[PTY 读取失败] " + ex.Message + "\n");
                    break;
                }
            }
        }, token);
    }

    private static void TrySendWindowChange(ShellStream shell, int columns, int rows, int width, int height)
    {
        try
        {
            var field = typeof(ShellStream).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic);
            var channel = field?.GetValue(shell);
            var method = channel?.GetType().GetMethod(
                "SendWindowChangeRequest",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(uint), typeof(uint), typeof(uint), typeof(uint)],
                modifiers: null);
            method?.Invoke(channel, [(uint)columns, (uint)rows, (uint)width, (uint)height]);
        }
        catch
        {
            // Some SSH.NET builds do not expose the channel. Existing PTY keeps working.
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static string NormalizeSha256Fingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.StartsWith("SHA256:", StringComparison.Ordinal) ? value : "SHA256:" + value;
    }
}
