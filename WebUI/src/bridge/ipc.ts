import type { S2CMessage, C2SMessage } from '../types/messages';

type MessageHandler = (msg: S2CMessage) => void;

const messageHandlers = new Set<MessageHandler>();

export function onMessage(handler: MessageHandler): () => void {
  messageHandlers.add(handler);
  return () => { messageHandlers.delete(handler); };
}

function dispatch(msg: S2CMessage): void {
  for (const handler of messageHandlers) {
    handler(msg);
  }
}

// Expose bridge internals for C# diagnostics / debugging
const _wb = (window as any);
_wb.__bridgeDispatch = dispatch;
_wb.__bridgeHandlerSet = (): boolean => messageHandlers.size > 0;
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
