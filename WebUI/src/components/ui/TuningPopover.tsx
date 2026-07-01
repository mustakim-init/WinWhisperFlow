import * as Popover from '@radix-ui/react-popover';
import { ChevronDown } from 'lucide-react';
import { useState } from 'react';
import { send } from '../../bridge/ipc';
import { useStore } from '../../hooks/useStore';
import { Switch } from './Switch';
import { Slider } from './Slider';

function isDml(device: string) { return device === 'dml'; }

interface TuningRowProps {
  label: string;
  children: React.ReactNode;
}

function TuningRow({ label, children }: TuningRowProps) {
  return (
    <div className="flex items-center justify-between gap-4 py-2.5 first:pt-0 last:pb-0 border-b border-border/20 last:border-0">
      <span className="text-sm text-muted-foreground shrink-0 min-w-[100px]">{label}</span>
      <div className="flex-1 max-w-[220px]">
        {children}
      </div>
    </div>
  );
}

export function TuningPopover() {
  const store = useStore();
  const [open, setOpen] = useState(false);
  const dml = store.modelLoaded && isDml(store.device);

  const sendSetting = (key: string, value: number | boolean | string | null) => {
    send({ type: 'set_setting', key, value, profile: store.activeProfile });
  };

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button
          type="button"
          className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors px-3 py-1.5 rounded-full hover:bg-muted/30 shrink-0"
        >
          Tuning
          <ChevronDown className={`w-3.5 h-3.5 transition-transform duration-200 ${open ? 'rotate-180' : ''}`} />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          side="top"
          align="end"
          sideOffset={10}
          className="z-50 w-80 rounded-xl border border-border/60 bg-popover p-4 text-popover-foreground shadow-lg
            data-[state=open]:animate-in data-[state=closed]:animate-out
            data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0
            data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95
            data-[side=bottom]:slide-in-from-top-2 data-[side=top]:slide-in-from-bottom-2"
        >
          <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-3">
            {store.activeProfile === 'voice' ? 'Voice' : 'Music'} Tuning
          </p>

          {dml && (
            <p className="text-xs text-amber-400/80 mb-3 leading-relaxed">
              DML model — only No-Speech and Log-Prob thresholds are supported.
            </p>
          )}

          <div className="space-y-0">
            {dml ? (
              <>
                <TuningRow label="No-Speech Thresh">
                  <Slider
                    value={store.noSpeechThreshold}
                    min={0.1}
                    max={1}
                    step={0.05}
                    onChange={(v) => sendSetting('no_speech_threshold', v)}
                    formatValue={(v) => v.toFixed(2)}
                  />
                </TuningRow>
                <TuningRow label="Log-Prob Thresh">
                  <Slider
                    value={store.logProbThreshold}
                    min={-5}
                    max={0}
                    step={0.1}
                    onChange={(v) => sendSetting('log_prob_threshold', v)}
                    formatValue={(v) => v.toFixed(1)}
                  />
                </TuningRow>
              </>
            ) : (
              <>
                <TuningRow label="Beam Size">
                  <Slider
                    value={store.beamSize}
                    min={1}
                    max={10}
                    step={1}
                    onChange={(v) => sendSetting('beam_size', v)}
                    formatValue={(v) => String(v)}
                  />
                </TuningRow>
                <TuningRow label="Temperature">
                  <Slider
                    value={store.temperature}
                    min={0}
                    max={1}
                    step={0.1}
                    onChange={(v) => sendSetting('temperature', v)}
                    formatValue={(v) => v.toFixed(1)}
                  />
                </TuningRow>
                <TuningRow label="VAD Filter">
                  <Switch checked={store.vadFilter} onCheckedChange={(v) => sendSetting('vad_filter', v)} />
                </TuningRow>
                <TuningRow label="No-Speech Thresh">
                  <Slider
                    value={store.noSpeechThreshold}
                    min={0.1}
                    max={1}
                    step={0.05}
                    onChange={(v) => sendSetting('no_speech_threshold', v)}
                    formatValue={(v) => v.toFixed(2)}
                  />
                </TuningRow>
                <TuningRow label="Log-Prob Thresh">
                  <Slider
                    value={store.logProbThreshold}
                    min={-5}
                    max={0}
                    step={0.1}
                    onChange={(v) => sendSetting('log_prob_threshold', v)}
                    formatValue={(v) => v.toFixed(1)}
                  />
                </TuningRow>
                <TuningRow label="Repetition Penalty">
                  <Slider
                    value={store.repetitionPenalty}
                    min={1}
                    max={5}
                    step={0.1}
                    onChange={(v) => sendSetting('repetition_penalty', v)}
                    formatValue={(v) => v.toFixed(1)}
                  />
                </TuningRow>
              </>
            )}
          </div>

          <p className="text-xs text-muted-foreground/40 mt-3 pt-3 border-t border-border/20 text-center">
            Settings auto-save per profile (Voice/Speech vs Music)
          </p>

          <Popover.Arrow className="fill-border" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
