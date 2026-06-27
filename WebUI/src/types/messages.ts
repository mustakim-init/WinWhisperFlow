export interface InitMessage {
  type: 'init';
  darkMode: boolean;
  ready: boolean;
  loaded?: boolean;
  error?: string;
  model?: string;
  device?: string;
  gpuName?: string;
  audioDevices?: string[];
  audioDeviceIndex?: number;
}

export interface SetupProgress {
  type: 'setup_progress';
  steps: SetupStep[];
  overall: number;
}

export interface SetupStep {
  id: string;
  label: string;
  status: 'pending' | 'running' | 'done' | 'error';
  error?: string;
}

export interface ModelDownloadProgress {
  type: 'model_download_progress';
  model: string;
  downloaded: number;
  total: number;
  status: 'downloading' | 'done' | 'error';
  error?: string;
  compositeName?: string;
  speed?: number;
}

export interface ModelsStatus {
  type: 'models_status';
  models: ModelInfo[];
}

export interface ModelInfo {
  name: string;
  displayName: string;
  size: number;
  downloaded: boolean;
  loaded: boolean;
  provider: string;
}

export interface FileTranscribeProgress {
  type: 'file_transcribe_progress';
  status: 'picking' | 'extracting' | 'analyzing' | 'separating' | 'transcribing' | 'done' | 'error' | 'cancelled';
  message: string;
  progress?: number;
  elapsed?: number;
  fileName?: string;
}

export type S2CMessage =
  | InitMessage
  | SetupProgress
  | ModelDownloadProgress
  | ModelsStatus
  | { type: 'status_update'; text: string; variant: 'success' | 'warning' | 'error' }
  | { type: 'transcription_result'; text: string; meta: string; isPartial?: boolean }
  | { type: 'audio_level'; level: number }
  | { type: 'log'; message: string }
  | { type: 'model_loaded'; model: string; device: string; note?: string }
  | { type: 'model_note'; note: string }
  | { type: 'listening_status'; listening: boolean }
  | { type: 'phone_mic_url'; url: string }
  | { type: 'phone_mic_status'; running: boolean }
  | { type: 'history_entry'; entry: HistoryEntry }
  | { type: 'notification'; title: string; message: string; variant: 'info' | 'warning' | 'error' }
  | { type: 'settings'; settings: Record<string, unknown> }
  | { type: 'clear_history' }
  | { type: 'directory_picked'; path: string | null }
  | FileTranscribeProgress;

export interface HistoryEntry {
  action: string;
  text: string;
  timestamp: string;
  ts: string;
  source?: string;
}

export type C2SMessage =
  | { type: 'bridge_ready' }
  | { type: 'toggle_listening' }
  | { type: 'load_model'; model: string }
  | { type: 'phone_mic_toggle' }
  | { type: 'set_setting'; key: string; value: unknown }
  | { type: 'set_language'; language: string }
  | { type: 'copy_text'; text: string }
  | { type: 'download_model'; model: string }
  | { type: 'delete_model'; model: string }
  | { type: 'cancel_download'; model: string }
  | { type: 'pause_download'; model: string }
  | { type: 'resume_download'; model: string }
  | { type: 'get_models_status' }
  | { type: 'get_model_note'; model: string }
  | { type: 'setup_runtime' }
  | { type: 'delete_history_entry'; ts: string; text: string }
  | { type: 'pick_directory'; purpose: string }
  | { type: 'open_directory'; path: string }
  | { type: 'open_url'; url: string }
  | { type: 'transcribe_file'; musicMode?: boolean }
  | { type: 'transcribe_file_path'; path: string; musicMode?: boolean }
  | { type: 'transcribe_dropped_file'; data: string; name: string }
  | { type: 'cancel_file_transcribe' };
