import type { HistoryEntry, SetupStep, ModelInfo } from '../types/messages';
import { send } from '../bridge/ipc';

type Listener = () => void;

export interface SettingProfile {
  beamSize: number; temperature: number; vadFilter: boolean;
  noSpeechThreshold: number; logProbThreshold: number;
  bestOf: number; repetitionPenalty: number; noRepeatNgramSize: number;
  lengthPenalty: number; compressionRatioThreshold: number;
  promptResetOnTemperature: number; conditionOnPreviousText: boolean;
  hotwords: string | null; hallucinationSilenceThreshold: number;
}

const PROFILE_KEYS: (keyof SettingProfile)[] = [
  'beamSize', 'temperature', 'vadFilter', 'noSpeechThreshold', 'logProbThreshold',
  'bestOf', 'repetitionPenalty', 'noRepeatNgramSize', 'lengthPenalty',
  'compressionRatioThreshold', 'promptResetOnTemperature', 'conditionOnPreviousText',
  'hotwords', 'hallucinationSilenceThreshold',
];

const CAML_TO_SNAKE: Record<string, string> = {
  beamSize: 'beam_size', temperature: 'temperature', vadFilter: 'vad_filter',
  noSpeechThreshold: 'no_speech_threshold', logProbThreshold: 'log_prob_threshold',
  bestOf: 'best_of', repetitionPenalty: 'repetition_penalty',
  noRepeatNgramSize: 'no_repeat_ngram_size', lengthPenalty: 'length_penalty',
  compressionRatioThreshold: 'compression_ratio_threshold',
  promptResetOnTemperature: 'prompt_reset_on_temperature',
  conditionOnPreviousText: 'condition_on_previous_text',
  hotwords: 'hotwords', hallucinationSilenceThreshold: 'hallucination_silence_threshold',
};

const SNAKE_TO_CAM: Record<string, keyof SettingProfile> = {};
for (const [cam, snake] of Object.entries(CAML_TO_SNAKE)) {
  SNAKE_TO_CAM[snake] = cam as keyof SettingProfile;
}

// Convert a snake_case profile object (from backend IPC) to camelCase (store format)
export function snakeToCamelProfile(src: Record<string, unknown>): Partial<SettingProfile> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(src)) {
    const cam = SNAKE_TO_CAM[k];
    if (cam) out[cam] = v;
  }
  return out as Partial<SettingProfile>;
}

export function reconcileProfile() {
  const profileKey = state.activeProfile === 'voice' ? 'voiceDefaults' : 'musicDefaults';
  const src = state[profileKey] as SettingProfile;
  set({
    beamSize: src.beamSize,
    temperature: src.temperature,
    vadFilter: src.vadFilter,
    noSpeechThreshold: src.noSpeechThreshold,
    logProbThreshold: src.logProbThreshold,
    bestOf: src.bestOf,
    repetitionPenalty: src.repetitionPenalty,
    noRepeatNgramSize: src.noRepeatNgramSize,
    lengthPenalty: src.lengthPenalty,
    compressionRatioThreshold: src.compressionRatioThreshold,
    promptResetOnTemperature: src.promptResetOnTemperature,
    conditionOnPreviousText: src.conditionOnPreviousText,
    hotwords: src.hotwords,
    hallucinationSilenceThreshold: src.hallucinationSilenceThreshold,
  });
}

export function loadProfile(name: 'voice' | 'music') {
  const oldKey = state.activeProfile === 'voice' ? 'voiceDefaults' : 'musicDefaults';
  const newKey = name === 'voice' ? 'voiceDefaults' : 'musicDefaults';
  const src = state[newKey] as SettingProfile;

  const patch: Partial<AppState> = { activeProfile: name, ...src };

  if (oldKey !== newKey) {
    patch[oldKey] = {
      beamSize: state.beamSize,
      temperature: state.temperature,
      vadFilter: state.vadFilter,
      noSpeechThreshold: state.noSpeechThreshold,
      logProbThreshold: state.logProbThreshold,
      bestOf: state.bestOf,
      repetitionPenalty: state.repetitionPenalty,
      noRepeatNgramSize: state.noRepeatNgramSize,
      lengthPenalty: state.lengthPenalty,
      compressionRatioThreshold: state.compressionRatioThreshold,
      promptResetOnTemperature: state.promptResetOnTemperature,
      conditionOnPreviousText: state.conditionOnPreviousText,
      hotwords: state.hotwords,
      hallucinationSilenceThreshold: state.hallucinationSilenceThreshold,
    } as SettingProfile;
  }

  set(patch);

  send({ type: 'switch_profile', profile: name });
}

export const DEFAULT_VOICE: SettingProfile = {
  beamSize: 1, temperature: 0, vadFilter: false,
  noSpeechThreshold: 0.45, logProbThreshold: -0.8,
  bestOf: 5, repetitionPenalty: 1, noRepeatNgramSize: 0,
  lengthPenalty: 1, compressionRatioThreshold: 2.4,
  promptResetOnTemperature: 0.5, conditionOnPreviousText: true,
  hotwords: null, hallucinationSilenceThreshold: 0,
};

export const DEFAULT_MUSIC: SettingProfile = {
  beamSize: 5, temperature: 0, vadFilter: false,
  noSpeechThreshold: 0.6, logProbThreshold: -1.0,
  bestOf: 5, repetitionPenalty: 1.2, noRepeatNgramSize: 3,
  lengthPenalty: 1, compressionRatioThreshold: 2.4,
  promptResetOnTemperature: 0.5, conditionOnPreviousText: false,
  hotwords: null, hallucinationSilenceThreshold: 2,
};

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
  voiceTranscript: string;
  voiceMeta: string;
  voicePartialTranscript: string;
  voicePartialMeta: string;
  fileTranscript: string;
  fileMeta: string;
  musicTranscript: string;
  musicMeta: string;
  fileMusicMode: boolean;
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
  detectedDevice: string;
  detectedGpuName: string;
  cpuName: string;
  cpuCores: number;
  cpuThreads: number;
  totalRam: number;
  fileTranscribing: boolean;
  fileName: string | null;
  fileProgress: number;
  fileStage: string;
  fileElapsed: number;
  updateAvailable: boolean;
  updateVersion: string | null;
  updateDownloading: boolean;
  updateProgress: number;
  updateReady: boolean;
  updateError: string | null;
  beamSize: number;
  temperature: number;
  vadFilter: boolean;
  noSpeechThreshold: number;
  logProbThreshold: number;
  bestOf: number;
  repetitionPenalty: number;
  noRepeatNgramSize: number;
  lengthPenalty: number;
  compressionRatioThreshold: number;
  promptResetOnTemperature: number;
  conditionOnPreviousText: boolean;
  hotwords: string | null;
  hallucinationSilenceThreshold: number;
  activeProfile: 'voice' | 'music';
  voiceDefaults: SettingProfile;
  musicDefaults: SettingProfile;
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
  detectedDevice: 'cpu',
  detectedGpuName: '',
  cpuName: '',
  cpuCores: 0,
  cpuThreads: 0,
  totalRam: 0,
  fileTranscribing: false,
  fileName: null,
  fileProgress: 0,
  fileStage: '',
  fileElapsed: 0,
  voiceTranscript: '',
  voiceMeta: '',
  voicePartialTranscript: '',
  voicePartialMeta: '',
  fileTranscript: '',
  fileMeta: '',
  musicTranscript: '',
  musicMeta: '',
  fileMusicMode: false,
  updateAvailable: false,
  updateVersion: null as string | null,
  updateDownloading: false,
  updateProgress: 0,
  updateReady: false,
  updateError: null as string | null,
  beamSize: 1,
  temperature: 0,
  vadFilter: false,
  noSpeechThreshold: 0.45,
  logProbThreshold: -0.8,
  bestOf: 5,
  repetitionPenalty: 1,
  noRepeatNgramSize: 0,
  lengthPenalty: 1,
  compressionRatioThreshold: 2.4,
  promptResetOnTemperature: 0.5,
  conditionOnPreviousText: true,
  hotwords: null,
  hallucinationSilenceThreshold: 0,
  activeProfile: 'voice',
  voiceDefaults: { ...DEFAULT_VOICE },
  musicDefaults: { ...DEFAULT_MUSIC },
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

export function set(patch: Partial<AppState>) {
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
export function setDetectedDevice(d: string) { set({ detectedDevice: d }); }
export function setDetectedGpuName(n: string) { set({ detectedGpuName: n }); }
export function setCpuName(n: string) { set({ cpuName: n }); }
export function setCpuCores(c: number) { set({ cpuCores: c }); }
export function setCpuThreads(t: number) { set({ cpuThreads: t }); }
export function setTotalRam(r: number) { set({ totalRam: r }); }
export function setFileTranscribing(v: boolean) { set({ fileTranscribing: v }); }
export function setFileName(n: string | null) { set({ fileName: n }); }
export function setFileProgress(p: number) { set({ fileProgress: p }); }
export function setFileStage(s: string) { set({ fileStage: s }); }
export function setFileElapsed(e: number) { set({ fileElapsed: e }); }
export function resetFileState() { set({ fileTranscribing: false, fileName: null, fileProgress: 0, fileStage: '', fileElapsed: 0 }); }
export function setVoiceTranscript(t: string, meta: string) { set({ voiceTranscript: t, voiceMeta: meta, voicePartialTranscript: '', voicePartialMeta: '' }); }
export function setVoicePartialTranscript(t: string, meta: string) { set({ voicePartialTranscript: t, voicePartialMeta: meta }); }
export function setFileTranscript(t: string, meta: string) { set({ fileTranscript: t, fileMeta: meta }); }
export function setMusicTranscript(t: string, meta: string) { set({ musicTranscript: t, musicMeta: meta }); }
export function setFileMusicMode(v: boolean) { set({ fileMusicMode: v }); }
export function setUpdateAvailable(v: boolean, ver: string | null) { set({ updateAvailable: v, updateVersion: ver }); }
export function setUpdateDownloading(v: boolean) { set({ updateDownloading: v }); }
export function setUpdateProgress(p: number) { set({ updateProgress: p }); }
export function setUpdateReady(v: boolean) { set({ updateReady: v }); }
export function setUpdateError(e: string | null) { set({ updateError: e }); }
// Skips profile update — used during backend sync to avoid corrupting profiles
// with stale flat values from a different profile on restart
export function setFlatOnly(key: keyof SettingProfile, value: any) {
  set({ [key]: value } as any);
}

function setWithProfile(key: keyof SettingProfile, value: any) {
  const profileKey = state.activeProfile === 'voice' ? 'voiceDefaults' : 'musicDefaults';
  set({ [key]: value, [profileKey]: { ...state[profileKey], [key]: value } } as any);
}

export function setProfileSetting(profile: 'voice' | 'music', key: keyof SettingProfile, value: any) {
  const profileKey = profile === 'voice' ? 'voiceDefaults' : 'musicDefaults';
  const patch: Record<string, any> = { [profileKey]: { ...state[profileKey], [key]: value } };
  if (profile === state.activeProfile) patch[key] = value;
  set(patch as any);
  const snake = CAML_TO_SNAKE[key];
  if (profile === state.activeProfile) {
    send({ type: 'set_setting', key: snake, value, profile });
  } else {
    send({ type: 'save_profile', profile, values: { [snake]: value } });
  }
}

export function setBeamSize(v: number) { setWithProfile('beamSize', v); }
export function setTemperature(v: number) { setWithProfile('temperature', v); }
export function setVadFilter(v: boolean) { setWithProfile('vadFilter', v); }
export function setNoSpeechThreshold(v: number) { setWithProfile('noSpeechThreshold', v); }
export function setLogProbThreshold(v: number) { setWithProfile('logProbThreshold', v); }
export function setBestOf(v: number) { setWithProfile('bestOf', v); }
export function setRepetitionPenalty(v: number) { setWithProfile('repetitionPenalty', v); }
export function setNoRepeatNgramSize(v: number) { setWithProfile('noRepeatNgramSize', v); }
export function setLengthPenalty(v: number) { setWithProfile('lengthPenalty', v); }
export function setCompressionRatioThreshold(v: number) { setWithProfile('compressionRatioThreshold', v); }
export function setPromptResetOnTemperature(v: number) { setWithProfile('promptResetOnTemperature', v); }
export function setConditionOnPreviousText(v: boolean) { setWithProfile('conditionOnPreviousText', v); }
export function setHotwords(v: string | null) { setWithProfile('hotwords', v); }
export function setHallucinationSilenceThreshold(v: number) { setWithProfile('hallucinationSilenceThreshold', v); }

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
