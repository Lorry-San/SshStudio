export type HostProfile = {
  id: string;
  name: string;
  address: string;
  port: number;
  user: string;
  status: string;
  fingerprint: string;
  cpu: number;
  memory: number;
  bandwidth: string;
};

export type ApiConfig = {
  baseUrl: string;
  model: string;
  apiMode: "responses" | "chat_completions";
  executionMode: "suggest" | "confirm" | "auto_safe";
  enableWebSearch: boolean;
  commandTimeoutSeconds: number;
  systemPrompt: string;
  reviewPrompt: string;
};

export type ChatMessage = {
  role: "user" | "assistant" | "system";
  text: string;
  tone?: "normal" | "safe" | "warning" | "danger";
};

export type AiAction = {
  type: "command" | "final";
  message: string;
  command?: string;
  risk?: "safe" | "medium" | "danger";
};
