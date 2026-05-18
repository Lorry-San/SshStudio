import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { FitAddon } from "@xterm/addon-fit";
import { Terminal } from "@xterm/xterm";
import "@xterm/xterm/css/xterm.css";
import "./styles.css";
import type { AiAction, ApiConfig, ChatMessage, HostProfile } from "./types";

const app = document.querySelector<HTMLDivElement>("#app");
if (!app) {
  throw new Error("Missing #app");
}
const appRoot = app;

let hosts: HostProfile[] = [];
let selectedHostId = "";
let apiConfig: ApiConfig | null = null;
let pendingCommand = "";
let messages: ChatMessage[] = [
  {
    role: "assistant",
    text: "我会围绕当前主机保留上下文。自动执行前会走独立 AI 审核提示词。",
    tone: "normal"
  }
];

const terminal = new Terminal({
  cursorBlink: true,
  fontFamily: '"JetBrains Mono", "Cascadia Mono", Consolas, monospace',
  fontSize: 13,
  theme: {
    background: "#07111f",
    foreground: "#d7e5f3",
    cursor: "#f7c948",
    selectionBackground: "#24547a",
    black: "#07111f",
    blue: "#5bb7ff",
    cyan: "#31d0aa",
    green: "#73d673",
    magenta: "#d38bff",
    red: "#ff6b6b",
    white: "#d7e5f3",
    yellow: "#f7c948"
  }
});
const fitAddon = new FitAddon();
terminal.loadAddon(fitAddon);

function selectedHost(): HostProfile | undefined {
  return hosts.find((host) => host.id === selectedHostId);
}

function render() {
  const host = selectedHost();
  appRoot.innerHTML = `
    <main class="shell">
      <header class="topbar">
        <section>
          <div class="eyebrow">Rust + Tauri WebView branch</div>
          <h1>SshStudio</h1>
        </section>
        <nav class="top-actions">
          <button class="status">${host?.status ?? "未连接"}</button>
          <button id="settingsBtn">API / 审核</button>
          <button id="exportBtn">迁移配置</button>
        </nav>
      </header>

      <section class="workspace">
        <aside class="hosts panel">
          <div class="panel-title">
            <span>主机</span>
            <button id="addHostBtn">+</button>
          </div>
          <input class="search" placeholder="搜索主机 / IP / 用户" />
          <div class="host-list">
            ${hosts.map(renderHostCard).join("")}
          </div>
        </aside>

        <section class="terminal-panel panel">
          <div class="terminal-head">
            <div>
              <strong>${host ? `${host.user}@${host.address}:${host.port}` : "未选择主机"}</strong>
              <span>${host?.fingerprint || "首次连接后记录 SSH 指纹"}</span>
            </div>
            <div class="terminal-actions">
              <button id="connectBtn">连接</button>
              <button id="resetFingerprintBtn">重置指纹</button>
            </div>
          </div>
          <div id="terminal"></div>
          <form id="commandForm" class="command-line">
            <input id="commandInput" placeholder="输入 PTY 内容或 shell 命令..." />
            <button>发送</button>
          </form>
        </section>

        <aside class="ai panel">
          <div class="panel-title">
            <span>AI 运维助手</span>
            <small>${apiConfig?.apiMode === "chat_completions" ? "Chat Completions" : "Responses"}</small>
          </div>
          <div class="chat">
            ${messages.map(renderMessage).join("")}
          </div>
          ${pendingCommand ? renderPendingCommand() : ""}
          <form id="aiForm" class="ai-input">
            <textarea id="aiInput" rows="3" placeholder="让 AI 检查 CPU、磁盘、服务或日志..."></textarea>
            <button>发送</button>
          </form>
        </aside>
      </section>
    </main>
  `;

  const terminalNode = document.querySelector<HTMLDivElement>("#terminal");
  if (terminalNode) {
    terminal.open(terminalNode);
    queueMicrotask(() => fitAddon.fit());
  }

  bindEvents();
}

function renderHostCard(host: HostProfile) {
  const selected = host.id === selectedHostId ? "selected" : "";
  return `
    <button class="host-card ${selected}" data-host-id="${host.id}">
      <div>
        <strong>${host.name}</strong>
        <span>${host.user}@${host.address}:${host.port}</span>
      </div>
      <em>${host.status}</em>
      <div class="meters">
        <label>CPU <b>${Math.round(host.cpu)}%</b></label>
        <label>内存 <b>${Math.round(host.memory)}%</b></label>
      </div>
      <small>${host.bandwidth}</small>
    </button>
  `;
}

function renderMessage(message: ChatMessage) {
  return `
    <article class="message ${message.tone ?? "normal"}">
      <strong>${message.role}</strong>
      <p>${escapeHtml(message.text)}</p>
    </article>
  `;
}

function renderPendingCommand() {
  return `
    <section class="review-card">
      <div>
        <strong>命令审核</strong>
        <span>自动模式下先交给 AI 审核；rm/rmdir 本地硬拦截。</span>
      </div>
      <pre>${escapeHtml(pendingCommand)}</pre>
      <div class="review-actions">
        <button id="approveCommandBtn">审核并执行</button>
        <button id="clearCommandBtn" type="button">清空</button>
      </div>
    </section>
  `;
}

function bindEvents() {
  document.querySelectorAll<HTMLButtonElement>("[data-host-id]").forEach((button) => {
    button.addEventListener("click", () => {
      selectedHostId = button.dataset.hostId ?? selectedHostId;
      render();
    });
  });

  document.querySelector<HTMLButtonElement>("#connectBtn")?.addEventListener("click", connectSelectedHost);
  document.querySelector<HTMLButtonElement>("#resetFingerprintBtn")?.addEventListener("click", () => {
    const host = selectedHost();
    if (!host) return;
    host.fingerprint = "";
    render();
  });

  document.querySelector<HTMLFormElement>("#commandForm")?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const input = document.querySelector<HTMLInputElement>("#commandInput");
    const command = input?.value.trim() ?? "";
    if (!command) return;
    input!.value = "";
    await runCommand(command);
  });

  document.querySelector<HTMLFormElement>("#aiForm")?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const input = document.querySelector<HTMLTextAreaElement>("#aiInput");
    const prompt = input?.value.trim() ?? "";
    if (!prompt) return;
    input!.value = "";
    await askAi(prompt);
  });

  document.querySelector<HTMLButtonElement>("#approveCommandBtn")?.addEventListener("click", async () => {
    const command = pendingCommand;
    pendingCommand = "";
    render();
    await runCommand(command);
  });

  document.querySelector<HTMLButtonElement>("#clearCommandBtn")?.addEventListener("click", () => {
    pendingCommand = "";
    render();
  });

  document.querySelector<HTMLButtonElement>("#settingsBtn")?.addEventListener("click", () => {
    messages.push({
      role: "system",
      text: "API/审核配置窗口将在下一阶段接入；Rust 后端模型已预留字段。",
      tone: "warning"
    });
    render();
  });
}

async function connectSelectedHost() {
  const host = selectedHost();
  if (!host) return;
  const updated = await invoke<HostProfile>("connect_host", { id: host.id });
  hosts = hosts.map((item) => (item.id === updated.id ? updated : item));
  render();
}

async function runCommand(command: string) {
  terminal.write(`\r\n$ ${command}\r\n`);
  const output = await invoke<string>("run_command", { command });
  terminal.write(output.replaceAll("\n", "\r\n"));
}

async function askAi(prompt: string) {
  messages.push({ role: "user", text: prompt, tone: "normal" });
  const raw = await invoke<string>("ask_ai", { prompt });
  const action = JSON.parse(raw) as AiAction;
  messages.push({ role: "assistant", text: action.message, tone: action.risk === "danger" ? "danger" : "safe" });
  if (action.type === "command" && action.command) {
    pendingCommand = action.command;
    if (apiConfig?.executionMode === "auto_safe") {
      const review = await invoke<{ allow: boolean; risk: string; reason: string }>("review_command", {
        command: action.command
      });
      messages.push({
        role: "assistant",
        text: `审核结果：${review.allow ? "通过" : "拒绝"}。${review.reason}`,
        tone: review.allow ? "safe" : "warning"
      });
      if (review.allow && review.risk !== "danger") {
        pendingCommand = "";
        await runCommand(action.command);
      }
    }
  }
  render();
}

function escapeHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

async function bootstrap() {
  [hosts, apiConfig] = await Promise.all([
    invoke<HostProfile[]>("load_hosts"),
    invoke<ApiConfig>("load_api_config")
  ]);
  selectedHostId = hosts[0]?.id ?? "";
  terminal.writeln("SshStudio Tauri 原型已启动。");
  terminal.writeln("Rust 后端接口已就绪，SSH/PTTY/vault 将在下一阶段接入。");
  await listen<string>("terminal-output", (event) => {
    terminal.write(event.payload.replaceAll("\n", "\r\n"));
  });
  render();
}

void bootstrap();
