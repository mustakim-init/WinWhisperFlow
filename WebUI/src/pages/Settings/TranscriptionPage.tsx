import React, { useEffect, useState } from 'react';
import { RotateCcw } from 'lucide-react';
import { Select } from '../../components/ui/Select';
import { Switch } from '../../components/ui/Switch';
import { Input } from '../../components/ui/Input';
import { Slider } from '../../components/ui/Slider';
import { InfoPopover } from '../../components/ui/InfoPopover';
import { send } from '../../bridge/ipc';
import { useStore } from '../../hooks/useStore';
import { SettingRow, SettingSection } from '../../components/ui/SettingRow';
import { setProfileSetting, SettingProfile, DEFAULT_VOICE, DEFAULT_MUSIC } from '../../lib/store';

// ── Setting definitions ────────────────────────────────────────────────
interface SettingDef {
  key: string;
  label: string;
  description: string;
  recommended: string;
}

const SETTINGS: SettingDef[] = [
  { key: 'beam_size', label: 'Beam Size',
    description: 'How many guesses the AI makes for each word. More guesses catch the right word more often, but it takes longer. For regular talking, 1 guess is plenty. For music with tricky lyrics, 5 guesses work better.',
    recommended: '1 for speech, 5 for music' },
  { key: 'temperature', label: 'Temperature',
    description: 'How creative the AI is with its word choices. 0 means it picks the most obvious word every time. Higher numbers let it try different words, which helps with accents, singing, or background noise.',
    recommended: '0 for speech and music' },
  { key: 'best_of', label: 'Best Of',
    description: 'When temperature is above 0, the AI tries this many versions and picks the best one. More tries = better but slower.',
    recommended: '5' },
  { key: 'no_speech_threshold', label: 'No-Speech Threshold',
    description: 'How sure the AI needs to be that someone is talking before it keeps a segment. Lower numbers cut more quiet parts.',
    recommended: '0.45 for speech, 0.6 for music' },
  { key: 'log_prob_threshold', label: 'Log-Prob Threshold',
    description: 'Confidence check for each word. Lower numbers only keep words the AI is very sure about.',
    recommended: '-0.8 for speech, -1.0 for music' },
  { key: 'compression_ratio_threshold', label: 'Compression Ratio',
    description: 'Catches AI hallucinations. If a segment compresses too easily, it gets thrown out.',
    recommended: '2.4' },
  { key: 'repetition_penalty', label: 'Repetition Penalty',
    description: 'Punishes the AI for repeating words. Higher = less repetition. Helps with music.',
    recommended: '1.0 for speech, 1.2 for music' },
  { key: 'no_repeat_ngram_size', label: 'No-Repeat N-Gram',
    description: 'Stops the AI from repeating phrases. 0 means no restriction.',
    recommended: '0 for speech, 3 for music' },
  { key: 'length_penalty', label: 'Length Penalty',
    description: 'Bias toward shorter or longer sentences. 1.0 is neutral.',
    recommended: '1.0' },
  { key: 'prompt_reset_on_temperature', label: 'Prompt Reset Temp',
    description: 'If temperature goes above this, the AI forgets previous context and starts fresh.',
    recommended: '0.5' },
  { key: 'hallucination_silence_threshold', label: 'Hallucination Silence',
    description: 'If the AI talks during silence for this many seconds, it is probably hallucinating. 0 = off.',
    recommended: '0 (off) for speech, 2 for music' },
];

const BOOL_SETTINGS: SettingDef[] = [
  { key: 'vad_filter', label: 'VAD Filter',
    description: 'Automatically skips parts where nobody is talking. Great for speech. Turn OFF for music or singing.',
    recommended: 'ON for speech, OFF for music' },
  { key: 'condition_on_previous_text', label: 'Condition on Previous Text',
    description: 'Lets the AI peek at what it just wrote for context. Turn OFF if you see looping.',
    recommended: 'ON for speech, OFF for music' },
];

// Map snake_case → camelCase store property names
const SNAKE_TO_CAM: Record<string, keyof SettingProfile> = {
  beam_size: 'beamSize', temperature: 'temperature', best_of: 'bestOf',
  no_speech_threshold: 'noSpeechThreshold', log_prob_threshold: 'logProbThreshold',
  compression_ratio_threshold: 'compressionRatioThreshold',
  repetition_penalty: 'repetitionPenalty', no_repeat_ngram_size: 'noRepeatNgramSize',
  length_penalty: 'lengthPenalty', prompt_reset_on_temperature: 'promptResetOnTemperature',
  hallucination_silence_threshold: 'hallucinationSilenceThreshold',
  vad_filter: 'vadFilter', condition_on_previous_text: 'conditionOnPreviousText',
  hotwords: 'hotwords',
};

function isDml(device: string) { return device === 'dml'; }

const RANGES: Record<string, { min: number; max: number; step: number }> = {
  beam_size: { min: 1, max: 10, step: 1 },
  temperature: { min: 0, max: 1, step: 0.1 },
  best_of: { min: 1, max: 10, step: 1 },
  no_speech_threshold: { min: 0.1, max: 1, step: 0.05 },
  log_prob_threshold: { min: -5, max: 0, step: 0.1 },
  compression_ratio_threshold: { min: 1, max: 10, step: 0.1 },
  repetition_penalty: { min: 1, max: 5, step: 0.1 },
  no_repeat_ngram_size: { min: 0, max: 10, step: 1 },
  length_penalty: { min: 0.5, max: 2, step: 0.1 },
  prompt_reset_on_temperature: { min: 0.5, max: 1, step: 0.05 },
  hallucination_silence_threshold: { min: 0, max: 10, step: 1 },
};

function fmtSlider(key: string, v: number): string {
  if (key === 'temperature') return v.toFixed(1);
  if (key === 'no_speech_threshold') return v.toFixed(2);
  if (key === 'log_prob_threshold') return v.toFixed(1);
  if (key === 'repetition_penalty') return v.toFixed(1);
  if (key === 'best_of') return String(v);
  return String(v);
}

// ── Component ─────────────────────────────────────────────────────────────
export function TranscriptionSettingsPage() {
  const store = useStore();
  const [editingProfile, setEditingProfile] = useState<'voice' | 'music'>('voice');
  const [hotwordInput, setHotwordInput] = useState(store.hotwords ?? '');
  const device = store.device || 'cpu';

  const profile = editingProfile === 'voice' ? store.voiceDefaults : store.musicDefaults;
  const defaults = editingProfile === 'voice' ? DEFAULT_VOICE : DEFAULT_MUSIC;

  const sendSetting = (key: string, value: number | boolean | string | null) => {
    const cam = SNAKE_TO_CAM[key];
    if (cam) setProfileSetting(editingProfile, cam, value);
  };

  const handleLanguageChange = (v: string) => {
    send({ type: 'set_language', language: v === 'auto' ? '' : v });
  };

  const handleModelChange = (v: string) => {
    send({ type: 'load_model', model: v });
  };

  const handleHotwordsBlur = () => {
    const val = hotwordInput.trim();
    sendSetting('hotwords', val || null);
  };

  useEffect(() => { send({ type: 'get_models_status' }); }, []);

  const downloadedModels = store.availableModels
    .filter((m) => m.downloaded)
    .map((m) => ({ label: m.displayName, value: m.name }));

  const numSlider = (def: SettingDef) => {
    const cam = SNAKE_TO_CAM[def.key];
    const val = profile[cam] as number;
    const dflt = defaults[cam] as number;
    const range = RANGES[def.key] || { min: 0, max: 1, step: 0.1 };
    const isDefault = Math.abs(val - dflt) < 1e-9;
    return (
      <SettingRow key={def.key}
        title={
          <div className="flex items-center gap-1.5">
            <span>{def.label}</span>
            <InfoPopover title={def.label} description={def.description} recommended={def.recommended} />
          </div>
        }
      >
        <div className="flex items-center gap-3">
          <Slider value={val} min={range.min} max={range.max} step={range.step}
            onChange={(v) => sendSetting(def.key, v)}
            formatValue={(v) => fmtSlider(def.key, v)}
            className="flex-1"
          />
          <button type="button" onClick={() => sendSetting(def.key, dflt)}
            className={`p-1.5 rounded-md transition-colors hover:bg-muted/50 ${isDefault ? 'opacity-20' : 'opacity-60 hover:opacity-100'}`}
            title="Reset to default" aria-label={`Reset ${def.label}`}>
            <RotateCcw className="w-3.5 h-3.5" />
          </button>
        </div>
      </SettingRow>
    );
  };

  const boolControl = (def: SettingDef) => {
    const cam = SNAKE_TO_CAM[def.key];
    const val = profile[cam] as boolean;
    return (
      <SettingRow key={def.key}
        title={
          <div className="flex items-center gap-1.5">
            <span>{def.label}</span>
            <InfoPopover title={def.label} description={def.description} recommended={def.recommended} />
          </div>
        }
        action={<Switch checked={val} onCheckedChange={(v) => sendSetting(def.key, v)} />}
      />
    );
  };

  return (
    <div className="space-y-8 max-w-2xl">
      {store.modelLoaded && isDml(device) && (
        <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
          <strong>DML model detected.</strong> DirectML only supports Language, No-Speech Threshold,
          and Log-Prob Threshold. Switch to a CPU or CUDA model for full tuning control.
        </div>
      )}

      <SettingSection title="Model" description="Which AI model to use">
        <SettingRow title="Model" description="Larger models are more accurate but slower" action={
          <Select options={downloadedModels.length > 0 ? downloadedModels : [{ label: 'No models downloaded', value: '__none__' }]}
            value={store.model} onChange={handleModelChange} className="w-44" />
        } />
        <SettingRow title="Language" description="Lock to a language or let the AI auto-detect" action={
          <Select options={[
            { label: 'Auto detect', value: 'auto' },
            { label: 'English', value: 'en' },
            { label: 'Bengali', value: 'bn' },
          ]} value={store.language || 'auto'} onChange={handleLanguageChange} className="w-32" />
        } />
      </SettingSection>

      {/* Profile selector */}
      <div className="flex gap-2">
        <button type="button" onClick={() => setEditingProfile('voice')}
          className={`flex-1 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
            editingProfile === 'voice'
              ? 'bg-accent text-accent-foreground shadow-sm'
              : 'bg-muted/30 text-muted-foreground hover:bg-muted/50'
          }`}>
          Voice / Speech Defaults
        </button>
        <button type="button" onClick={() => setEditingProfile('music')}
          className={`flex-1 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
            editingProfile === 'music'
              ? 'bg-accent text-accent-foreground shadow-sm'
              : 'bg-muted/30 text-muted-foreground hover:bg-muted/50'
          }`}>
          Music Defaults
        </button>
      </div>

      <p className="text-xs text-muted-foreground -mt-4">
        Editing <strong>{editingProfile === 'voice' ? 'Voice/Speech' : 'Music'}</strong> defaults.
        {editingProfile !== store.activeProfile && (
          <span className="text-amber-400/70 ml-1">
            (This profile is not active — switch to a {editingProfile === 'voice' ? 'Voice' : 'Music'} tab in the Transcribe page to use these values.)
          </span>
        )}
      </p>

      {/* DML-limited — only gate when a DML model is actually loaded */}
      {store.modelLoaded && isDml(device) ? (
        <SettingSection title="DML-Compatible Settings" description="DirectML only supports these">
          {SETTINGS.filter(s => s.key === 'no_speech_threshold' || s.key === 'log_prob_threshold').map(numSlider)}
        </SettingSection>
      ) : (
        <>
          <SettingSection title="Filters" description="Pre-processing controls">
            {BOOL_SETTINGS.map(boolControl)}
          </SettingSection>
          <SettingSection title="Advanced Tuning" description="Fine-tune how the AI transcribes">
            {SETTINGS.map(numSlider)}
          </SettingSection>
          <SettingSection title="Extra" description="Additional controls">
            <SettingRow title={
              <div className="flex items-center gap-1.5">
                <span>Hotwords</span>
                <InfoPopover title="Hotwords"
                  description="Words or names the AI should pay extra attention to. Like giving it a cheat sheet."
                  recommended="Leave empty unless the AI keeps missing specific words" />
              </div>
            } action={
              <Input placeholder="e.g. Mustakim, WinWhisper"
                value={editingProfile === 'voice' ? hotwordInput : (profile.hotwords ?? '')}
                onChange={(e) => {
                  if (editingProfile === 'voice') setHotwordInput(e.target.value);
                  else sendSetting('hotwords', e.target.value || null);
                }}
                onBlur={() => { if (editingProfile === 'voice') handleHotwordsBlur(); }}
                className="w-44 h-9 text-sm" />
            } />
          </SettingSection>
        </>
      )}
    </div>
  );
}


