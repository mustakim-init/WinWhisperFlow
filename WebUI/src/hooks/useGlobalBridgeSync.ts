import { useEffect } from 'react';
import { onMessage, send } from '../bridge/ipc';
import type { S2CMessage } from '../types/messages';
import {
  setDevice, setGpuName, setAudioDevices, setAudioDeviceIndex, setModel, setIsListening, setIsReady, setModelLoaded as storeSetModelLoaded,
  setModelNote, setAudioLevel, setStatus, addHistory, addLog, clearHistory,
  setPhoneMicRunning, setPhoneMicUrl, setSetupSteps, setSetupOverall, setSetupError,
  setLanguage, setModelLoading, setAvailableModels,
  setFileTranscribing, setFileName, setFileProgress, setFileStage, setFileElapsed, resetFileState,
  getState,
  setVoiceTranscript, setVoicePartialTranscript, setFileTranscript, setMusicTranscript,
  setDetectedDevice, setDetectedGpuName, setCpuName, setCpuCores, setCpuThreads, setTotalRam,
  setUpdateAvailable, setUpdateDownloading, setUpdateProgress, setUpdateReady, setUpdateError,
} from '../lib/store';
import { useUiStore } from '../stores/uiStore';
import { useDownloadStore } from '../stores/downloadStore';
import { useToastStore } from '../stores/toastStore';

export function useGlobalBridgeSync() {
  useEffect(() => {
    // Retry bridge_ready until init is received (handles race where
    // C# WebMessageReceived handler may not be registered yet)
    let retries = 0
    const maxRetries = 20
    const interval = setInterval(() => {
      send({ type: 'bridge_ready' })
      retries++
      if (retries >= maxRetries) clearInterval(interval)
    }, 250)
    send({ type: 'bridge_ready' })

    const unsubMsg = onMessage((msg: S2CMessage) => {
      // Stop bridge_ready retries as soon as we receive ANY message from C#.
      // The first message proves the WebView2 bridge is registered. Continued
      // retries would trigger duplicate RunHealthCheckAsync calls, each
      // cancelling the previous one and causing spurious "Setup failed" errors.
      clearInterval(interval)

      switch (msg.type) {
        case 'init':
          setSetupError(undefined);
          setModelLoading(false);
          if (msg.device) setDevice(msg.device);
          if (msg.model) setModel(msg.model);
          if (msg.gpuName) setGpuName(msg.gpuName);
          if (msg.audioDevices) setAudioDevices(msg.audioDevices);
          if (msg.audioDeviceIndex !== undefined) setAudioDeviceIndex(msg.audioDeviceIndex);
          if (msg.darkMode !== undefined) useUiStore.getState().setDarkMode(msg.darkMode);
          useUiStore.getState().setIsReady(msg.ready);
          setIsReady(msg.ready);
          // Set detected (detected) hardware info only once — never overwritten by model_loaded
          if (msg.detectedDevice) setDetectedDevice(msg.detectedDevice);
          if (msg.detectedGpuName) setDetectedGpuName(msg.detectedGpuName);
          if (msg.cpuName) setCpuName(msg.cpuName);
          if (msg.cpuCores !== undefined) setCpuCores(msg.cpuCores);
          if (msg.cpuThreads !== undefined) setCpuThreads(msg.cpuThreads);
          if (msg.totalRam !== undefined) setTotalRam(msg.totalRam);
          if (msg.ready && msg.error) {
            setStatus(msg.error, 'error');
            useUiStore.getState().setStatus(msg.error, 'error');
            useToastStore.getState().addToast({ title: 'Model not loaded', message: msg.error, variant: 'error' });
          } else if (!msg.ready && msg.error) {
            setSetupError(msg.error);
          }
          break;

        case 'settings': {
          const s = msg.settings;
          if (typeof s.language === 'string') setLanguage(s.language);
          break;
        }

        case 'clear_history':
          clearHistory();
          break;

        case 'status_update':
          setStatus(msg.text, msg.variant);
          useUiStore.getState().setStatus(msg.text, msg.variant);
          if (msg.text.includes('Loading model')) setModelLoading(true);
          if (msg.variant === 'success' || msg.variant === 'error') setModelLoading(false);
          break;

        case 'model_download_progress':
          if (msg.status === 'cancelled') {
            useDownloadStore.getState().clear(msg.compositeName ?? msg.model);
          } else {
            useDownloadStore.getState().setProgress(
              msg.compositeName ?? msg.model,
              msg.downloaded,
              msg.total,
              msg.status,
              msg.error,
              msg.speed,
            );
          }
          if (msg.status === 'done') {
            useToastStore.getState().addToast({
              title: `Model downloaded`,
              message: msg.compositeName ?? msg.model,
              variant: 'success',
            });
          } else if (msg.status === 'error') {
            useToastStore.getState().addToast({
              title: `Download failed`,
              message: msg.error ?? 'Unknown error',
              variant: 'error',
            });
          }
          break;

        case 'model_loaded':
          setModel(msg.model);
          storeSetModelLoaded(true);
          if (msg.device) setDevice(msg.device);
          if (msg.note) setModelNote(msg.note);
          useToastStore.getState().addToast({
            title: `Model loaded`,
            message: `${msg.model} on ${msg.device}`,
            variant: 'success',
          });
          break;

        case 'model_note':
          setModelNote(msg.note);
          break;

        case 'listening_status':
          setIsListening(msg.listening);
          if (msg.listening) setVoicePartialTranscript('', '');
          break;

        case 'audio_level':
          setAudioLevel(msg.level);
          break;

        case 'transcription_result': {
          const s = getState();
          if (s.fileTranscribing) {
            if (s.fileMusicMode) {
              setMusicTranscript(msg.text, msg.meta);
            } else {
              setFileTranscript(msg.text, msg.meta);
            }
          } else if (msg.isPartial) {
            setVoicePartialTranscript(msg.text, msg.meta);
          } else {
            setVoiceTranscript(msg.text, msg.meta);
          }
          break;
        }

        case 'history_entry':
          addHistory(msg.entry);
          break;

        case 'log':
          addLog(msg.message);
          break;

        case 'phone_mic_status':
          setPhoneMicRunning(msg.running);
          break;

        case 'phone_mic_url':
          setPhoneMicUrl(msg.url);
          break;

        case 'setup_progress':
          setSetupSteps(msg.steps);
          setSetupOverall(msg.overall);
          useUiStore.getState().setSetupSteps(msg.steps);
          if (msg.steps.every((s) => s.status === 'done')) {
            setSetupError(undefined);
          }
          const errStep = msg.steps.find((s) => s.status === 'error');
          if (errStep) {
            setSetupError(errStep.error || 'Setup failed');
          }
          break;

        case 'models_status':
          setAvailableModels(msg.models);
          break;

        case 'file_transcribe_progress':
          if (msg.fileName) setFileName(msg.fileName);
          if (msg.progress !== undefined) setFileProgress(msg.progress);
          if (msg.elapsed !== undefined) setFileElapsed(msg.elapsed);
          setFileStage(msg.status);
          if (msg.status === 'picking' || msg.status === 'extracting' || msg.status === 'analyzing' || msg.status === 'separating') {
            setFileTranscribing(true);
            setStatus(msg.message, 'warning');
          } else if (msg.status === 'transcribing') {
            setFileTranscribing(true);
            setStatus(msg.message, 'warning');
          } else if (msg.status === 'done') {
            resetFileState();
            setStatus('Transcription complete', 'success');
          } else if (msg.status === 'error') {
            resetFileState();
            setStatus('Transcription failed', 'error');
          }
          break;

        case 'update_available':
          setUpdateAvailable(msg.available, msg.version);
          break;

        case 'update_download_started':
          setUpdateDownloading(true);
          setUpdateProgress(0);
          setUpdateError(null);
          break;

        case 'update_download_progress':
          setUpdateProgress(msg.progress);
          break;

        case 'update_download_complete':
          setUpdateDownloading(false);
          setUpdateReady(true);
          break;

        case 'update_download_error':
          setUpdateDownloading(false);
          setUpdateError(msg.error);
          break;

        case 'notification':
          addLog(`[${msg.variant}] ${msg.title}: ${msg.message}`);
          useToastStore.getState().addToast({
            title: msg.title,
            message: msg.message,
            variant: msg.variant,
          });
          break;
      }
    });

    return () => { clearInterval(interval); unsubMsg(); };
  }, []);
}
