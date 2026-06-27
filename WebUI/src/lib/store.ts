import type { HistoryEntry, SetupStep, ModelInfo } from '../types/messages';

type Listener = () => void;

interface AppState {
  device: string;
  gpuName: string;
  audioDevices: string[];
  audioDeviceIndex: number;
  model: string;
  language: string;
  isListening: boolean;
  isReady: boolean;
  needsSetup: boolean;
  modelLoaded: boolean;
  modelLoading: boolean;
  modelNote: string;
  audioLevel: number;
  lastTranscript: string;
  lastMeta: string;
  partialTranscript: string;
  partialMeta: string;
  statusText: string | null;
  statusVariant: 'success' | 'warning' | 'error';
  history: HistoryEntry[];
  logs: LogEntry[];
  phoneMicRunning: boolean;
  phoneMicUrl: string;
  setupSteps: SetupStep[];
  setupOverall: number;
  setupError: string | undefined;
  availableModels: ModelInfo[];
  fileTranscribing: boolean;
  fileName: string | null;
  fileProgress: number;
  fileStage: string;
  fileElapsed: number;
}

export interface LogEntry {
  id: number;
  timestamp: number;
  message: string;
}

let nextLogId = 0;

let state: AppState = {
  device: 'cpu',
  gpuName: '',
  audioDevices: [],
  audioDeviceIndex: 0,
  model: 'small-cpu',
  language: 'en',
  isListening: false,
  isReady: false,
  needsSetup: false,
  modelLoaded: false,
  modelLoading: false,
  modelNote: '',
  audioLevel: 0,
  lastTranscript: '',
  lastMeta: '',
  partialTranscript: '',
  partialMeta: '',
  statusText: null,
  statusVariant: 'success',
  history: [],
  logs: [],
  phoneMicRunning: false,
  phoneMicUrl: 'Not started',
  setupSteps: [],
  setupOverall: 0,
  setupError: undefined,
  availableModels: [],
  fileTranscribing: false,
  fileName: null,
  fileProgress: 0,
  fileStage: '',
  fileElapsed: 0,
};

const listeners = new Set<Listener>();

function notify() {
  for (const l of listeners) l();
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

export function getState(): AppState {
  return state;
}

function set(patch: Partial<AppState>) {
  state = { ...state, ...patch };
  notify();
}

export function setDevice(d: string) { set({ device: d }); }
export function setGpuName(n: string) { set({ gpuName: n }); }
export function setAudioDevices(d: string[]) { set({ audioDevices: d }); }
export function setAudioDeviceIndex(i: number) { set({ audioDeviceIndex: i }); }
export function setModel(m: string) { set({ model: m }); }
export function setLanguage(l: string) { set({ language: l }); }
export function setIsListening(v: boolean) { set({ isListening: v }); }
export function setIsReady(v: boolean) { set({ isReady: v, needsSetup: !v }); }
export function setNeedsSetup(v: boolean) { set({ needsSetup: v }); }
export function setModelLoaded(v: boolean) { set({ modelLoaded: v, modelLoading: !v }); }
export function setModelLoading(v: boolean) { set({ modelLoading: v }); }
export function setModelNote(n: string) { set({ modelNote: n }); }
export function setAudioLevel(l: number) { set({ audioLevel: l }); }
export function setLastTranscript(t: string, meta: string) { set({ lastTranscript: t, lastMeta: meta, partialTranscript: '', partialMeta: '' }); }
export function setPartialTranscript(t: string, meta: string) { set({ partialTranscript: t, partialMeta: meta }); }
export function setStatus(text: string, variant: 'success' | 'warning' | 'error') { set({ statusText: text, statusVariant: variant }); }
export function setPhoneMicRunning(v: boolean) { set({ phoneMicRunning: v }); }
export function setPhoneMicUrl(u: string) { set({ phoneMicUrl: u }); }
export function setSetupSteps(s: SetupStep[]) { set({ setupSteps: s }); }
export function setSetupOverall(o: number) { set({ setupOverall: o }); }
export function setSetupError(e: string | undefined) { set({ setupError: e }); }
export function setAvailableModels(ms: ModelInfo[]) { set({ availableModels: ms }); }
export function setFileTranscribing(v: boolean) { set({ fileTranscribing: v }); }
export function setFileName(n: string | null) { set({ fileName: n }); }
export function setFileProgress(p: number) { set({ fileProgress: p }); }
export function setFileStage(s: string) { set({ fileStage: s }); }
export function setFileElapsed(e: number) { set({ fileElapsed: e }); }
export function resetFileState() { set({ fileTranscribing: false, fileName: null, fileProgress: 0, fileStage: '', fileElapsed: 0 }); }

export function addHistory(entry: HistoryEntry) {
  set({ history: [entry, ...state.history] });
}

export function addLog(message: string) {
  const entry: LogEntry = { id: nextLogId++, timestamp: Date.now(), message };
  const logs = [...state.logs, entry];
  if (logs.length > 2000) logs.splice(0, logs.length - 2000);
  set({ logs });
}

export function clearLogs() { set({ logs: [] }); }
export function clearHistory() { set({ history: [] }); }

export function removeHistory(index: number) {
  set({ history: state.history.filter((_, i) => i !== index) });
}
