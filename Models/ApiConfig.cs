namespace SshStudio.Models;

public sealed class ApiConfig
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5.4";
    public string ApiMode { get; set; } = "responses";
    public string ExecutionMode { get; set; } = "confirm";
    public bool EnableWebSearch { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 180;
    public string SystemPrompt { get; set; } = "你是 SSH 运维助手。";
    public string ReviewPrompt { get; set; } =
        "你是 SSH 命令安全审核器。只判断给定命令是否允许自动执行。允许自动执行的命令必须是只读诊断、查看状态、查看日志或列目录，不应修改文件、进程、服务、网络、防火墙、权限、用户、容器或集群状态。遇到不确定、复合命令、下载执行、提权、重定向写入、管道到解释器、删除、重启、停止服务、修改权限、修改配置，都必须拒绝。";
}
