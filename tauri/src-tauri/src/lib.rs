use serde::{Deserialize, Serialize};
use std::sync::Mutex;
use tauri::{Emitter, Manager, State};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HostProfile {
    pub id: String,
    pub name: String,
    pub address: String,
    pub port: u16,
    pub user: String,
    pub status: String,
    pub fingerprint: String,
    pub cpu: f64,
    pub memory: f64,
    pub bandwidth: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ApiConfig {
    pub base_url: String,
    pub model: String,
    pub api_mode: String,
    pub execution_mode: String,
    pub enable_web_search: bool,
    pub command_timeout_seconds: u64,
    pub system_prompt: String,
    pub review_prompt: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AiReviewResult {
    pub allow: bool,
    pub risk: String,
    pub reason: String,
}

#[derive(Default)]
pub struct AppState {
    hosts: Mutex<Vec<HostProfile>>,
    api_config: Mutex<ApiConfig>,
}

impl Default for ApiConfig {
    fn default() -> Self {
        Self {
            base_url: "https://api.openai.com/v1".into(),
            model: "gpt-5.4".into(),
            api_mode: "responses".into(),
            execution_mode: "confirm".into(),
            enable_web_search: false,
            command_timeout_seconds: 180,
            system_prompt: "你是 SSH 运维助手。".into(),
            review_prompt: "你是 SSH 命令安全审核器。只判断给定命令是否允许自动执行。允许自动执行的命令必须是只读诊断、查看状态、查看日志或列目录；不确定时拒绝。".into(),
        }
    }
}

#[tauri::command]
fn load_hosts(state: State<'_, AppState>) -> Result<Vec<HostProfile>, String> {
    let mut hosts = state.hosts.lock().map_err(|err| err.to_string())?;
    if hosts.is_empty() {
        hosts.push(HostProfile {
            id: "local-demo".into(),
            name: "示例主机".into(),
            address: "127.0.0.1".into(),
            port: 22,
            user: "root".into(),
            status: "未连接".into(),
            fingerprint: "".into(),
            cpu: 0.0,
            memory: 0.0,
            bandwidth: "--".into(),
        });
    }

    Ok(hosts.clone())
}

#[tauri::command]
fn load_api_config(state: State<'_, AppState>) -> Result<ApiConfig, String> {
    Ok(state
        .api_config
        .lock()
        .map_err(|err| err.to_string())?
        .clone())
}

#[tauri::command]
fn save_api_config(config: ApiConfig, state: State<'_, AppState>) -> Result<(), String> {
    *state.api_config.lock().map_err(|err| err.to_string())? = config;
    Ok(())
}

#[tauri::command]
async fn connect_host(id: String, app: tauri::AppHandle, state: State<'_, AppState>) -> Result<HostProfile, String> {
    let mut hosts = state.hosts.lock().map_err(|err| err.to_string())?;
    let host = hosts
        .iter_mut()
        .find(|host| host.id == id)
        .ok_or_else(|| "Host not found".to_string())?;

    host.status = "已连接".into();
    host.fingerprint = if host.fingerprint.is_empty() {
        "SHA256:demo-fingerprint-placeholder".into()
    } else {
        host.fingerprint.clone()
    };
    let connected = host.clone();
    let _ = app.emit("terminal-output", format!("[SSH] 已连接 {}\n", connected.address));
    Ok(connected)
}

#[tauri::command]
async fn review_command(command: String, _state: State<'_, AppState>) -> Result<AiReviewResult, String> {
    let lower = command.trim().to_lowercase();
    if lower == "rm" || lower.starts_with("rm ") || lower.starts_with("rmdir ") {
        return Ok(AiReviewResult {
            allow: false,
            risk: "danger".into(),
            reason: "本地硬拦截删除命令。".into(),
        });
    }

    Ok(AiReviewResult {
        allow: true,
        risk: "safe".into(),
        reason: "Tauri 原型暂用本地模拟审核；后续接入 OpenAI 审核接口。".into(),
    })
}

#[tauri::command]
async fn run_command(command: String, app: tauri::AppHandle) -> Result<String, String> {
    let output = format!("$ {command}\nTauri 原型已收到命令；真实 SSH PTY 将在后续接入。\n");
    let _ = app.emit("terminal-output", output.clone());
    Ok(output)
}

#[tauri::command]
async fn ask_ai(prompt: String) -> Result<String, String> {
    Ok(format!(
        "{{\"type\":\"command\",\"message\":\"原型建议先查看系统状态。\",\"command\":\"uptime\",\"risk\":\"safe\",\"intent\":\"{}\"}}",
        prompt.replace('"', "\\\"")
    ))
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_shell::init())
        .manage(AppState::default())
        .invoke_handler(tauri::generate_handler![
            ask_ai,
            connect_host,
            load_api_config,
            load_hosts,
            review_command,
            run_command,
            save_api_config
        ])
        .setup(|app| {
            let window = app.get_webview_window("main").ok_or("main window missing")?;
            window.set_title("SshStudio Tauri")?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
