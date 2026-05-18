using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SshStudio.Models;

namespace SshStudio.Services;

public sealed class AiClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new();

    public async Task<AiAction> RequestActionAsync(
        ApiConfig config,
        string userPrompt,
        string terminalContext,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException("API Key 为空。");
        }

        var endpoint = config.BaseUrl.TrimEnd('/') + "/responses";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(BuildPayload(config, userPrompt, terminalContext), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = new StringBuilder();
        var data = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                await ConsumeSseDataAsync(data.ToString(), raw, onDelta).ConfigureAwait(false);
                data.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }
                data.Append(line[5..].Trim());
            }
        }

        if (data.Length > 0)
        {
            await ConsumeSseDataAsync(data.ToString(), raw, onDelta).ConfigureAwait(false);
        }

        return ParseAction(raw.ToString());
    }

    private static object BuildPayload(ApiConfig config, string userPrompt, string terminalContext)
    {
        var input = new object[]
        {
            new { role = "system", content = BuildSystemPrompt(config) },
            new { role = "user", content = BuildUserPrompt(userPrompt, terminalContext) }
        };

        if (!config.EnableWebSearch)
        {
            return new
            {
                model = config.Model,
                stream = true,
                input
            };
        }

        return new
        {
            model = config.Model,
            stream = true,
            tools = new object[]
            {
                new { type = "web_search" }
            },
            input
        };
    }

    private static async Task ConsumeSseDataAsync(string data, StringBuilder raw, Func<string, Task>? onDelta)
    {
        if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
        {
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            return;
        }
        using (document)
        {
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "";

        string? text = null;
        if ((type.Contains("output_text", StringComparison.OrdinalIgnoreCase) ||
             type.Contains("text.delta", StringComparison.OrdinalIgnoreCase)) &&
            root.TryGetProperty("delta", out var delta))
        {
            text = delta.GetString();
        }
        else if (root.TryGetProperty("response", out var response) &&
                 response.TryGetProperty("output", out var output))
        {
            text = ExtractOutputText(output);
        }
        else if (root.TryGetProperty("output", out var directOutput))
        {
            text = ExtractOutputText(directOutput);
        }

        if (!string.IsNullOrEmpty(text))
        {
            raw.Append(text);
            if (onDelta is not null)
            {
                await onDelta(text).ConfigureAwait(false);
            }
        }
        }
    }

    private static string ExtractOutputText(JsonElement output)
    {
        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    builder.Append(text.GetString());
                }
            }
        }
        return builder.ToString();
    }

    private static AiAction ParseAction(string raw)
    {
        var json = ExtractFirstJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AiAction { Type = "final", Message = raw.Trim() };
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new AiAction
        {
            Type = GetString(root, "type", "final"),
            Message = GetString(root, "message", ""),
            Intent = GetString(root, "intent", ""),
            Command = GetString(root, "command", ""),
            Risk = GetString(root, "risk", "safe"),
            Wait = GetBool(root, "wait", true),
            ReturnAfter = GetBool(root, "return_after", true),
            Next = GetString(root, "next", GetString(root, "next_action", ""))
        };
    }

    private static string ExtractFirstJsonObject(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = raw.IndexOf('\n');
            var lastFence = raw.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && lastFence > firstLine)
            {
                raw = raw[(firstLine + 1)..lastFence].Trim();
            }
        }

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '{')
            {
                continue;
            }

            var candidate = TryReadJsonObject(raw, i);
            if (candidate is null)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return candidate;
                }
            }
            catch (JsonException)
            {
                // Keep scanning: model output may contain examples or multiple JSON objects.
            }
        }

        return "";
    }

    private static string? TryReadJsonObject(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
            }
            else if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string GetString(JsonElement root, string name, string fallback)
    {
        return root.TryGetProperty(name, out var value) ? value.GetString() ?? fallback : fallback;
    }

    private static bool GetBool(JsonElement root, string name, bool fallback)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static string BuildSystemPrompt(ApiConfig config)
    {
        return config.SystemPrompt + "\n" +
               "你在一个 SSH 图形工具里。你可以请求执行当前服务器命令，但必须只输出一个 JSON 对象，不要 Markdown 包裹，不要额外文本。" +
               "命令动作格式：{\"type\":\"command\",\"message\":\"给用户看的中文说明\",\"intent\":\"目的\",\"command\":\"shell 命令\",\"risk\":\"safe|medium|danger\",\"wait\":true,\"return_after\":true,\"next\":\"拿到输出后继续分析什么\"}。" +
               "最终回答格式：{\"type\":\"final\",\"message\":\"中文 Markdown 结论\",\"risk\":\"safe\"}。" +
               "用户要求查看、检查、分析资源、配置、服务或日志时，优先返回 command。危险命令必须 risk=danger。" +
               (config.EnableWebSearch
                   ? "如果问题涉及最新版本、漏洞公告、软件文档、报错资料或互联网资料，可以使用互联网搜索工具；搜索后仍必须按上述 JSON 格式输出。"
                   : "");
    }

    private static string BuildUserPrompt(string prompt, string terminalContext)
    {
        return "用户请求：\n" + prompt + "\n\n当前终端最近输出：\n" + terminalContext;
    }
}
