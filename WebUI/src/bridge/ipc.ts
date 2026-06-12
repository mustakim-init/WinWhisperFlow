export type C2SMessage =
  | { type: 'bridge_ready' }
  | { type: 'transcribe'; audioPath: string; language?: string }
  | { type: 'toggle_listening' }
  | { type: 'load_model'; model: string; gpu: boolean }
  | { type: 'setup_runtime' }
  | { type: 'phone_mic_toggle' }
  | { type: 'get_settings' }
  | { type: 'set_setting'; key: string; value: unknown }
  | { type: 'startup_toggle'; enabled: boolean }
  | { type: 'get_model_note'; model: string; gpu: boolean }
  | { type: 'set_language'; language: string }
  | { type: 'copy_text'; text: string };

export interface InitMessage {
  type: 'init';
  darkMode: boolean;
  loaded: boolean;
  error?: string;
  model?: string;
  device?: string;
}

export type S2CMessage =
  | InitMessage
  | { type: 'status_update'; text: string; variant: 'success' | 'warning' | 'error' }
  | { type: 'transcription_result'; text: string; meta: string; isPartial?: boolean }
  | { type: 'audio_level'; level: number }
  | { type: 'log'; message: string }
  | { type: 'model_loaded'; model: string; device: string; note?: string }
  | { type: 'model_note'; note: string }
  | { type: 'listening_status'; listening: boolean }
  | { type: 'phone_mic_url'; url: string }
  | { type: 'phone_mic_status'; running: boolean }
  | { type: 'hardware_info'; text: string }
  | { type: 'history_entry'; entry: HistoryEntry }
  | { type: 'notification'; title: string; message: string; variant: 'info' | 'warning' | 'error' }
  | { type: 'settings'; settings: Record<string, unknown> };

export interface HistoryEntry {
  action: string;
  text: string;
  timestamp: string;
}

type MessageHandler = (msg: S2CMessage) => void;
type ReadyCallback = () => void;

let messageHandler: MessageHandler | null = null;
let readyCallback: ReadyCallback | null = null;

export function onMessage(handler: MessageHandler): void {
  messageHandler = handler;
}

export function onReady(cb: ReadyCallback): void {
  readyCallback = cb;
}

function dispatch(msg: S2CMessage): void {
  if (msg.type === 'init') {
    readyCallback?.();
  }
  messageHandler?.(msg);
}

// Expose bridge internals for C# diagnostics / debugging
const _wb = (window as any);
_wb.__bridgeDispatch = dispatch;
_wb.__bridgeHandlerSet = (): boolean => messageHandler !== null;
_wb.__bridgeSendMsg = (type: string, payload?: any): void => {
  send({ type, ...payload } as any);
};

export function send(msg: C2SMessage): void {
  try {
    (window as { chrome?: { webview?: { postMessage: (data: string) => void } } }).chrome?.webview?.postMessage?.(JSON.stringify(msg));
  } catch {
    console.warn('WebView2 bridge not available');
  }
}

export function setup(): void {
  try {
    const wv = (window as { chrome?: { webview?: { addEventListener?: (event: string, handler: (e: unknown) => void) => void } } }).chrome?.webview;
    if (wv?.addEventListener) {
      wv.addEventListener('message', (event: any) => {
        try {
          const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
          dispatch(data as S2CMessage);
        } catch (e: any) {
          console.error('bridge msg error:', e?.message || e);
        }
      });
    } else {
      console.warn('chrome.webview.addEventListener not available');
    }
  } catch (e: any) {
    console.error('bridge setup error:', e?.message || e);
  }
}
