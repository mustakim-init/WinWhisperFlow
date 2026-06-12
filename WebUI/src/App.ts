import { send, onMessage, onReady, type S2CMessage, type InitMessage } from './bridge/ipc';
import QRCode from 'qrcode';

interface ListItemData {
  action: string;
  text: string;
  timestamp: string;
}

const state = {
  isListening: false,
  isBusy: false,
  activeModel: '',
  activeDevice: 'cpu',
  gpuEnabled: false,
  modelNote: '',
  audioLevel: 0,
  lastResult: '',
  lastMeta: 'No transcription yet',
  history: [] as ListItemData[],
  phoneMicRunning: false,
  phoneMicUrl: 'Not started',
  selectedModel: 'small',
  selectedLanguage: 'en',
};

const modelOptions = [
  { label: 'Tiny (fastest)', value: 'tiny' },
  { label: 'Base (fast)', value: 'base' },
  { label: 'Small (recommended)', value: 'small' },
  { label: 'Medium (accurate)', value: 'medium' },
  { label: 'Large v3 (best quality)', value: 'large-v3' },
  { label: 'Turbo (fast + quality)', value: 'turbo' },
];

const languageOptions = [
  { label: 'English', value: 'en' },
  { label: 'Bangla', value: 'bn' },
  { label: 'Auto detect', value: '' },
];

export function buildApp(container: HTMLElement): void {
  // Generate the main HTML structure
  container.innerHTML = `
    <div class="flex flex-col h-full bg-background text-on-background font-sans overflow-hidden">
      <!-- Top Bar -->
      <div class="flex items-center gap-3 px-5 py-3 bg-surface-container border-b border-outline-variant shrink-0 select-none">
        <div class="w-10 h-10 rounded-xl bg-gradient-to-br from-primary to-primary-container flex items-center justify-center shrink-0 shadow-sm">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="var(--md-sys-color-on-primary)" stroke-width="2" stroke-linecap="round"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" x2="12" y1="19" y2="22"/></svg>
        </div>
        <div class="flex-1 flex items-center gap-3 min-w-0">
          <div class="text-lg font-semibold tracking-tight">WinWhisper Flow</div>
          <div id="hw-chip" class="hidden items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[11px] font-bold bg-primary-container text-on-primary-container whitespace-nowrap">
            <span class="w-1.5 h-1.5 rounded-full bg-current"></span>
            <span id="hw-chip-text"></span>
          </div>
        </div>
        <div id="pill-slot" class="mx-1 flex items-center">
          <div id="status-pill" class="px-3 py-1 text-xs font-medium rounded-full bg-surface-variant text-on-surface-variant">Starting...</div>
        </div>
      </div>

      <!-- Main Scroll Body -->
      <div class="flex-1 overflow-y-auto overflow-x-hidden p-5 space-y-4">
        
        <!-- Controls Card -->
        <div class="bg-surface-container-high rounded-2xl p-5 shadow-sm border border-outline-variant/30">
          <div class="flex items-center gap-2 mb-4">
            <div class="w-1 h-4 rounded-full bg-primary"></div>
            <h2 class="text-sm font-semibold uppercase tracking-wider text-primary">Recording Control</h2>
          </div>
          
          <div class="mb-5">
            <div class="text-sm font-medium mb-2 flex justify-between">
              <span>Microphone Level</span>
              <span id="level-hint" class="text-xs text-on-surface-variant">Speak to test</span>
            </div>
            <div class="h-2 w-full bg-surface-container-highest rounded-full overflow-hidden">
              <div id="mic-level-bar" class="h-full bg-primary w-0 transition-all duration-75 ease-out rounded-full"></div>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label class="block text-xs font-medium text-on-surface-variant mb-1">Language</label>
              <select id="lang-select" class="w-full bg-surface-container-highest border border-outline-variant text-sm rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-primary focus:border-transparent transition-shadow">
                ${languageOptions.map(o => `<option value="${o.value}" ${state.selectedLanguage === o.value ? 'selected' : ''}>${o.label}</option>`).join('')}
              </select>
            </div>
            <div>
              <label class="block text-xs font-medium text-on-surface-variant mb-1">Model Size</label>
              <select id="model-select" class="w-full bg-surface-container-highest border border-outline-variant text-sm rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-primary focus:border-transparent transition-shadow">
                ${modelOptions.map(o => `<option value="${o.value}" ${state.selectedModel === o.value ? 'selected' : ''}>${o.label}</option>`).join('')}
              </select>
            </div>
          </div>

          <div id="model-note" class="text-xs text-on-surface-variant mb-4 empty:hidden"></div>

          <div class="flex items-center justify-between mb-5 bg-surface-container p-3 rounded-lg border border-outline-variant/50">
            <div class="text-sm font-medium">GPU Acceleration</div>
            <label class="relative inline-flex items-center cursor-pointer">
              <input type="checkbox" id="gpu-switch" class="sr-only peer" ${state.gpuEnabled ? 'checked' : ''}>
              <div class="w-11 h-6 bg-surface-variant peer-focus:outline-none peer-focus:ring-2 peer-focus:ring-primary rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-primary"></div>
            </label>
          </div>

          <div id="model-warning-banner" class="hidden mb-4 bg-tertiary-container/30 border border-tertiary text-on-tertiary-container p-3 rounded-lg flex items-start gap-2">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" class="mt-0.5 shrink-0"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
            <div class="text-xs font-medium leading-relaxed">
              Model configuration changed. You must click <strong>Reload Model</strong> to apply these settings.
            </div>
          </div>

          <div class="flex flex-wrap gap-2">
            <button id="toggle-btn" class="flex-1 bg-primary text-on-primary hover:bg-primary/90 font-semibold py-2.5 px-4 rounded-xl transition-all active:scale-[0.98] disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
              Start Listening
            </button>
            <button id="reload-btn" class="bg-surface-container-highest text-on-surface hover:bg-surface-variant font-medium py-2.5 px-4 rounded-xl border border-outline-variant transition-all active:scale-[0.98] disabled:opacity-50 disabled:cursor-not-allowed">
              Reload Model
            </button>
            <button id="setup-btn" class="bg-surface-container-highest text-on-surface hover:bg-surface-variant font-medium py-2.5 px-4 rounded-xl border border-outline-variant transition-all active:scale-[0.98] disabled:opacity-50 disabled:cursor-not-allowed">
              Setup
            </button>
          </div>
        </div>

        <!-- Phone Mic Card -->
        <div class="bg-surface-container-high rounded-2xl p-5 shadow-sm border border-outline-variant/30">
          <div class="flex items-center gap-2 mb-2">
            <div class="w-1 h-4 rounded-full bg-secondary"></div>
            <h2 class="text-sm font-semibold uppercase tracking-wider text-secondary">Phone Mic</h2>
          </div>
          <p class="text-sm text-on-surface-variant mb-4">Use your phone as a high-quality wireless microphone over local Wi-Fi.</p>
          
          <div class="flex flex-col sm:flex-row gap-4 items-start sm:items-center">
            <button id="phone-btn" class="shrink-0 bg-secondary-container text-on-secondary-container hover:bg-secondary-container/80 font-medium py-2 px-4 rounded-xl transition-all active:scale-[0.98] flex items-center gap-2">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="5" y="2" width="14" height="20" rx="2" ry="2"/><path d="M12 18h.01"/></svg>
              <span id="phone-btn-text">Start Server</span>
            </button>
            
            <div class="flex-1 min-w-0 w-full flex items-center gap-2 bg-surface-container px-3 py-2 rounded-lg border border-outline-variant/50">
              <div id="phone-url" class="flex-1 font-mono text-xs text-on-surface truncate select-all" title="${state.phoneMicUrl}">${state.phoneMicUrl}</div>
              <button id="copy-url-btn" class="text-primary hover:text-primary/80 text-xs font-medium px-2 uppercase tracking-wider shrink-0 transition-colors">Copy</button>
            </div>
          </div>
          
          <div id="qr-container" class="hidden mt-4 flex items-center justify-center p-4 bg-white rounded-xl max-w-[200px] mx-auto">
            <img id="qr-img" src="" alt="QR Code" class="w-full h-auto" />
          </div>
          <div id="qr-hint" class="hidden text-center text-xs text-on-surface-variant mt-2">Scan with your phone's camera. You may need to bypass the local HTTPS warning.</div>
        </div>

        <!-- Result Card -->
        <div class="bg-surface-container-high rounded-2xl p-5 shadow-sm border border-outline-variant/30">
          <div class="flex items-center justify-between mb-4">
            <div class="flex items-center gap-2">
              <div class="w-1 h-4 rounded-full bg-tertiary"></div>
              <h2 class="text-sm font-semibold uppercase tracking-wider text-tertiary">Result</h2>
            </div>
            <button id="copy-result-btn" class="text-primary hover:text-primary/80 text-xs font-medium uppercase tracking-wider transition-colors">Copy text</button>
          </div>

          <div class="bg-surface-container px-3 py-1.5 rounded-lg border border-outline-variant/50 mb-3 inline-block">
            <span id="last-meta" class="text-[11px] font-medium text-on-surface-variant uppercase tracking-wide">${state.lastMeta}</span>
          </div>

          <div id="last-result" class="min-h-[80px] p-4 bg-surface-container rounded-xl text-sm leading-relaxed whitespace-pre-wrap select-text border border-outline-variant/50">Transcription appears here...</div>

          <h3 class="text-xs font-medium text-on-surface-variant mt-6 mb-3 uppercase tracking-wider">History</h3>
          <div id="history-container" class="space-y-2 max-h-[200px] overflow-y-auto pr-1"></div>
        </div>

        <!-- Activity Log -->
        <div class="bg-surface-container-high rounded-2xl p-5 shadow-sm border border-outline-variant/30">
          <div class="flex items-center justify-between mb-3">
            <h2 class="text-sm font-semibold uppercase tracking-wider text-on-surface-variant">Activity Log</h2>
            <button id="clear-log-btn" class="text-on-surface-variant hover:text-on-surface text-xs font-medium transition-colors">Clear</button>
          </div>
          <div id="log-content" class="bg-[#0d1013] text-[#a0aab5] font-mono text-[10px] p-3 rounded-xl h-[120px] overflow-y-auto whitespace-pre-wrap border border-outline-variant/30 select-text leading-relaxed"></div>
        </div>

        <!-- Settings -->
        <div class="bg-surface-container-high rounded-2xl p-5 shadow-sm border border-outline-variant/30 mb-8">
          <h2 class="text-sm font-semibold uppercase tracking-wider text-on-surface-variant mb-4">System Settings</h2>
          <div class="flex items-center justify-between">
            <div class="text-sm font-medium">Start with Windows</div>
            <label class="relative inline-flex items-center cursor-pointer">
              <input type="checkbox" id="startup-switch" class="sr-only peer">
              <div class="w-11 h-6 bg-surface-variant peer-focus:outline-none peer-focus:ring-2 peer-focus:ring-primary rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-primary"></div>
            </label>
          </div>
          <p class="text-xs text-on-surface-variant mt-3">Global hotkeys: <strong>Ctrl+Alt+S</strong> to toggle listening.</p>
        </div>

      </div>
    </div>
  `;

  // --- Element References ---
  const toggleBtn = document.getElementById('toggle-btn') as HTMLButtonElement;
  const reloadBtn = document.getElementById('reload-btn') as HTMLButtonElement;
  const setupBtn = document.getElementById('setup-btn') as HTMLButtonElement;
  const phoneBtn = document.getElementById('phone-btn') as HTMLButtonElement;
  const phoneBtnText = document.getElementById('phone-btn-text')!;
  const langSelect = document.getElementById('lang-select') as HTMLSelectElement;
  const modelSelect = document.getElementById('model-select') as HTMLSelectElement;
  const gpuSwitch = document.getElementById('gpu-switch') as HTMLInputElement;
  const startupSwitch = document.getElementById('startup-switch') as HTMLInputElement;
  const copyUrlBtn = document.getElementById('copy-url-btn') as HTMLButtonElement;
  const copyResultBtn = document.getElementById('copy-result-btn') as HTMLButtonElement;
  const clearLogBtn = document.getElementById('clear-log-btn') as HTMLButtonElement;
  const qrContainer = document.getElementById('qr-container')!;
  const qrImg = document.getElementById('qr-img') as HTMLImageElement;
  const qrHint = document.getElementById('qr-hint')!;

  function showWarning() {
    document.getElementById('model-warning-banner')!.classList.remove('hidden');
  }

  function hideWarning() {
    document.getElementById('model-warning-banner')!.classList.add('hidden');
  }

  // --- Event Listeners ---
  toggleBtn.onclick = () => send({ type: 'toggle_listening' });
  reloadBtn.onclick = () => {
    setBusy(true, 'Loading model...');
    hideWarning();
    send({ type: 'load_model', model: state.selectedModel, gpu: state.gpuEnabled });
  };
  setupBtn.onclick = () => {
    setBusy(true, 'Setting up runtime...');
    hideWarning();
    send({ type: 'setup_runtime' });
  };
  phoneBtn.onclick = () => send({ type: 'phone_mic_toggle' });

  langSelect.onchange = () => {
    const v = langSelect.value;
    state.selectedLanguage = v;
    send({ type: 'set_language', language: v });
    send({ type: 'set_setting', key: 'language', value: v });
  };

  modelSelect.onchange = () => {
    const v = modelSelect.value;
    state.selectedModel = v;
    send({ type: 'get_model_note', model: v, gpu: state.gpuEnabled });
    if (v !== state.activeModel) showWarning();
    else hideWarning();
  };

  gpuSwitch.onchange = () => {
    const c = gpuSwitch.checked;
    state.gpuEnabled = c;
    send({ type: 'get_model_note', model: state.selectedModel, gpu: c });
    if (c !== (state.activeDevice !== 'cpu')) showWarning();
    else hideWarning();
  };

  startupSwitch.onchange = () => send({ type: 'set_setting', key: 'startup', value: startupSwitch.checked });

  copyUrlBtn.onclick = () => {
    const url = document.getElementById('phone-url')?.textContent ?? '';
    if (url && url !== 'Not started') send({ type: 'copy_text', text: url });
  };

  copyResultBtn.onclick = () => {
    const text = document.getElementById('last-result')?.textContent ?? '';
    if (text) send({ type: 'copy_text', text });
  };

  clearLogBtn.onclick = () => { document.getElementById('log-content')!.textContent = ''; };

  // --- Helpers ---
  function setBusy(busy: boolean, statusText?: string): void {
    state.isBusy = busy;
    reloadBtn.disabled = busy;
    setupBtn.disabled = busy;
    if (!state.isListening) toggleBtn.disabled = busy;
    if (busy && statusText) setStatus(statusText, 'bg-tertiary-container text-on-tertiary-container');
  }

  function setStatus(text: string, colorClass: string): void {
    const pill = document.getElementById('status-pill')!;
    pill.textContent = text;
    pill.className = `px-3 py-1 text-xs font-medium rounded-full ${colorClass}`;
  }

  function showBadge(model: string, device: string): void {
    const chip = document.getElementById('hw-chip')!;
    chip.classList.remove('hidden');
    chip.classList.add('flex');
    document.getElementById('hw-chip-text')!.textContent = `${model} · ${device.toUpperCase()}`;
  }

  function appendLog(message: string): void {
    const log = document.getElementById('log-content')!;
    const line = `[${new Date().toLocaleTimeString()}] ${message}`;
    log.textContent += (log.textContent ? '\n' : '') + line;
    log.scrollTop = log.scrollHeight;
  }

  function renderHistory(): void {
    const container = document.getElementById('history-container')!;
    container.innerHTML = '';
    if (state.history.length === 0) {
      container.innerHTML = `<div class="text-xs text-on-surface-variant italic">No transcriptions yet.</div>`;
      return;
    }

    for (const item of state.history) {
      const el = document.createElement('div');
      el.className = 'bg-surface-container-highest p-3 rounded-lg text-sm border border-outline-variant/30 flex flex-col gap-1';
      el.innerHTML = `
        <div class="flex items-center justify-between opacity-70 text-[10px] font-medium uppercase tracking-wider">
          <span class="text-primary">${item.action}</span>
          <span>${item.timestamp}</span>
        </div>
        <div class="text-on-surface whitespace-pre-wrap">${item.text}</div>
      `;
      container.appendChild(el);
    }
  }

  async function updatePhoneQr(url: string) {
    if (url === 'Not started' || !url) {
      qrContainer.classList.add('hidden');
      qrHint.classList.add('hidden');
      return;
    }
    try {
      const dataUrl = await QRCode.toDataURL(url, { margin: 1, color: { dark: '#000000', light: '#ffffff' } });
      qrImg.src = dataUrl;
      qrContainer.classList.remove('hidden');
      qrHint.classList.remove('hidden');
    } catch (e) {
      console.error('Failed to generate QR', e);
    }
  }

  // Initial State
  renderHistory();

  // --- IPC Events ---
  onReady(() => {
    send({ type: 'bridge_ready' });
  });

  onMessage((msg: S2CMessage) => {
    switch (msg.type) {
      case 'init': {
        const init = msg as InitMessage;
        if (init.darkMode) document.documentElement.classList.add('dark');
        else document.documentElement.classList.remove('dark');

        if (init.loaded) {
          setBusy(false);
          setStatus('Ready', 'bg-primary-container text-on-primary-container');
          const m = init.model || 'small';
          const d = init.device || 'cpu';
          showBadge(m, d);
          modelSelect.value = m;
          gpuSwitch.checked = (d !== 'cpu');
          state.gpuEnabled = (d !== 'cpu');
          state.selectedModel = m;
          state.activeModel = m;
          state.activeDevice = d;
        } else if (init.error) {
          setBusy(false);
          appendLog(init.error);
          setStatus('Setup required', 'bg-error-container text-on-error-container');
        }
        break;
      }
      case 'settings': {
        const settings = msg.settings as Record<string, unknown>;
        if (typeof settings.startup === 'boolean') startupSwitch.checked = settings.startup;
        if (typeof settings.language === 'string') {
          state.selectedLanguage = settings.language;
          langSelect.value = settings.language;
        }
        break;
      }
      case 'listening_status': {
        state.isListening = msg.listening;
        toggleBtn.disabled = state.isBusy;

        if (msg.listening) {
          toggleBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="6" width="12" height="12" rx="2"/></svg> Stop Listening`;
          toggleBtn.classList.replace('bg-primary', 'bg-error');
          toggleBtn.classList.replace('text-on-primary', 'text-on-error');
          toggleBtn.classList.replace('hover:bg-primary/90', 'hover:bg-error/90');
        } else {
          toggleBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg> Start Listening`;
          toggleBtn.classList.replace('bg-error', 'bg-primary');
          toggleBtn.classList.replace('text-on-error', 'text-on-primary');
          toggleBtn.classList.replace('hover:bg-error/90', 'hover:bg-primary/90');
        }
        break;
      }
      case 'status_update': {
        let color = 'bg-surface-variant text-on-surface-variant';
        if (msg.variant === 'success') color = 'bg-primary-container text-on-primary-container';
        if (msg.variant === 'warning') color = 'bg-tertiary-container text-on-tertiary-container';
        if (msg.variant === 'error') color = 'bg-error-container text-on-error-container';

        setStatus(msg.text, color);
        if (msg.variant === 'success' || msg.variant === 'error') {
          setBusy(false);
        } else if (msg.text.includes('Loading') || msg.text.includes('Setting up') || msg.text.includes('Transcribing')) {
          setBusy(true, msg.text);
        }
        break;
      }
      case 'transcription_result': {
        const r = document.getElementById('last-result')!;
        const m = document.getElementById('last-meta')!;

        if (msg.isPartial) {
          // Emphasize partial result slightly different (e.g., pulsing or italic)
          r.innerHTML = `<span class="opacity-80 italic">${msg.text}</span> <span class="inline-block w-2 h-4 bg-primary animate-pulse ml-1 align-middle"></span>`;
        } else {
          r.textContent = msg.text;
        }

        m.textContent = msg.meta;
        state.lastResult = msg.text;
        state.lastMeta = msg.meta;

        if (!msg.isPartial) setBusy(false);
        break;
      }
      case 'audio_level': {
        const fill = document.getElementById('mic-level-bar')!;
        const level = Math.min(100, msg.level * 100);
        fill.style.width = `${level}%`;
        const hint = document.getElementById('level-hint')!;
        if (level > 3) {
          hint.textContent = 'Audio detected';
          hint.classList.replace('text-on-surface-variant', 'text-primary');
        } else {
          hint.textContent = 'Speak to test';
          hint.classList.replace('text-primary', 'text-on-surface-variant');
        }
        break;
      }
      case 'log': {
        appendLog(msg.message);
        break;
      }
      case 'model_loaded': {
        state.activeModel = msg.model;
        state.activeDevice = msg.device;
        state.selectedModel = msg.model;
        state.gpuEnabled = (msg.device !== 'cpu');
        setBusy(false);
        showBadge(msg.model, msg.device);
        modelSelect.value = msg.model;
        gpuSwitch.checked = (msg.device !== 'cpu');
        if (msg.note) document.getElementById('model-note')!.textContent = msg.note;
        break;
      }
      case 'model_note': {
        document.getElementById('model-note')!.textContent = msg.note;
        break;
      }
      case 'phone_mic_url': {
        state.phoneMicUrl = msg.url;
        const box = document.getElementById('phone-url')!;
        box.textContent = msg.url;
        box.title = msg.url;
        updatePhoneQr(msg.url);
        break;
      }
      case 'phone_mic_status': {
        state.phoneMicRunning = msg.running;
        if (msg.running) {
          phoneBtn.classList.replace('bg-secondary-container', 'bg-error-container');
          phoneBtn.classList.replace('text-on-secondary-container', 'text-on-error-container');
          phoneBtn.classList.replace('hover:bg-secondary-container/80', 'hover:bg-error-container/80');
          phoneBtnText.textContent = 'Stop Server';
          phoneBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="6" y="6" width="12" height="12" rx="2"/></svg> Stop Server`;
        } else {
          phoneBtn.classList.replace('bg-error-container', 'bg-secondary-container');
          phoneBtn.classList.replace('text-on-error-container', 'text-on-secondary-container');
          phoneBtn.classList.replace('hover:bg-error-container/80', 'hover:bg-secondary-container/80');
          phoneBtnText.textContent = 'Start Server';
          phoneBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="5" y="2" width="14" height="20" rx="2" ry="2"/><path d="M12 18h.01"/></svg> Start Server`;
          updatePhoneQr('');
        }
        break;
      }
      case 'history_entry': {
        state.history.unshift({ action: msg.entry.action, text: msg.entry.text, timestamp: msg.entry.timestamp });
        renderHistory();
        break;
      }
    }
  });
}
