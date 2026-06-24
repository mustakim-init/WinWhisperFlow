import { AnimatePresence, motion } from 'framer-motion';
import { Check, Copy, Loader2, Mic, Square } from 'lucide-react';
import React, { useState } from 'react';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { Select } from '../components/ui/Select';
import { send } from '../bridge/ipc';
import { useStore } from '../hooks/useStore';

const languageOptions = [
  { label: 'Auto detect', value: 'auto' },
  { label: 'English', value: 'en' },
  { label: 'Bengali', value: 'bn' },
];

export function DictatePage() {
  const store = useStore();
  const [copied, setCopied] = useState(false);

  const downloadedModels = store.availableModels
    .filter((m) => m.downloaded)
    .map((m) => ({ label: m.displayName, value: m.name }));

  const handleToggle = () => send({ type: 'toggle_listening' });

  const handleModelChange = (v: string) => {
    send({ type: 'load_model', model: v });
    send({ type: 'get_model_note', model: v });
  };

  const handleLanguageChange = (v: string) => {
    const lang = v === 'auto' ? '' : v;
    send({ type: 'set_language', language: lang });
    send({ type: 'set_setting', key: 'language', value: lang });
  };

  const handleCopy = (text: string) => {
    send({ type: 'copy_text', text });
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  const barSeeds = React.useMemo(() => [0.7, 0.9, 0.6, 1.0, 0.8], []);

  const displayText = store.isListening
    ? store.partialTranscript
    : store.lastTranscript;

  const metaText = store.isListening ? store.partialMeta : store.lastMeta;

  return (
    <div className="flex flex-col h-full min-h-0 relative">
      {/* Header */}
      <div className="shrink-0 pt-8 pb-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold">Transcribe</h1>
            <p className="text-sm text-muted-foreground mt-0.5">Record and transcribe speech in real-time</p>
          </div>
          {metaText && (
            <Badge variant="outline" className="text-[10px] bg-accent/5 border-accent/20 text-accent">
              {metaText}
            </Badge>
          )}
        </div>
      </div>

      {/* Transcription area — the main focus, fills available space */}
          <div className="flex-1 min-h-0 overflow-hidden flex flex-col pb-28">
        <AnimatePresence mode="wait">
          {store.isListening || store.lastTranscript ? (
            <motion.div
              key="content"
              initial={{ opacity: 0, y: 5 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0 }}
              className="flex-1 min-h-0 flex flex-col"
            >
              <div className="flex-1 min-h-0 rounded-xl border border-border bg-muted/15 overflow-hidden flex flex-col">
                <div className="flex items-center justify-between px-6 py-3 border-b border-border bg-card/50 shrink-0">
                  <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                    {store.isListening ? 'Recording Live' : 'Transcription'}
                  </h2>
                  {store.lastTranscript && !store.isListening && (
                    <Button variant="ghost" size="sm" onClick={() => handleCopy(store.lastTranscript)} className="gap-1.5 h-7 text-xs text-muted-foreground hover:text-foreground">
                      {copied ? <Check size={12} /> : <Copy size={12} />} {copied ? 'Copied!' : 'Copy'}
                    </Button>
                  )}
                </div>

                <div className="flex-1 overflow-y-auto text-[15px] leading-relaxed whitespace-pre-wrap select-text p-6 text-foreground font-sans scrollbar-hide">
                  {displayText || (
                    <span className="text-muted-foreground italic">Transcription appears here...</span>
                  )}
                  {store.isListening && (
                    <motion.span
                      animate={{ opacity: [1, 0] }}
                      transition={{ repeat: Infinity, duration: 0.8 }}
                      className="inline-block w-0.5 h-4 bg-accent ml-1 align-middle"
                    />
                  )}
                </div>
              </div>
            </motion.div>
          ) : (
            <motion.div
              key="empty"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="flex-1 flex flex-col items-center justify-center border border-dashed border-border/80 rounded-xl text-sm text-muted-foreground select-none gap-3"
            >
              <Mic size={32} className="text-muted-foreground/30" />
              <span className="italic">Press the record button to start transcribing</span>
              <span className="text-xs text-muted-foreground/50">Drag-and-drop file transcription not yet implemented</span>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {/* Floating bottom control bar — always visible */}
      <div className="absolute bottom-0 left-0 right-0 z-20 pb-4 px-2 pointer-events-none">
        <div className="pointer-events-auto mx-auto max-w-2xl rounded-2xl border border-border bg-card/95 backdrop-blur-md shadow-lg px-4 py-3 flex items-center gap-3">
          {/* Record button */}
          <motion.button
            onClick={handleToggle}
            disabled={store.modelLoading}
            className={`relative shrink-0 w-11 h-11 rounded-full flex items-center justify-center transition-all duration-200 disabled:opacity-50 disabled:cursor-not-allowed ${
              store.isListening
                ? 'bg-red-500 text-white shadow-[0_0_16px_rgba(239,68,68,0.4)]'
                : 'bg-accent text-accent-foreground shadow-[0_0_12px_hsl(var(--accent)/0.3)]'
            }`}
            whileTap={{ scale: 0.92 }}
          >
            {store.isListening ? (
              <Square className="h-4 w-4 fill-current" />
            ) : (
              <Mic className="h-5 w-5" />
            )}
            {store.isListening && (
              <motion.span
                className="absolute inset-0 rounded-full bg-red-500"
                animate={{ scale: [1, 1.2, 1], opacity: [0.3, 0, 0.3] }}
                transition={{ repeat: Infinity, duration: 1.5, ease: 'easeInOut' }}
              />
            )}
          </motion.button>

          {/* Audio level bars — only when recording */}
          <AnimatePresence>
            {store.isListening && (
              <motion.div
                initial={{ opacity: 0, width: 0 }}
                animate={{ opacity: 1, width: 'auto' }}
                exit={{ opacity: 0, width: 0 }}
                className="flex items-center gap-[2px] h-8 overflow-hidden"
              >
                {barSeeds.map((seed, i) => (
                  <motion.div
                    key={i}
                    className="w-[3px] rounded-full bg-accent"
                    animate={{
                      height: [6, 6 + (store.audioLevel * 16 + 3) * seed, 6],
                    }}
                    transition={{
                      repeat: Infinity,
                      duration: 0.4 + i * 0.08,
                      ease: 'easeInOut',
                      delay: i * 0.06,
                    }}
                  />
                ))}
              </motion.div>
            )}
          </AnimatePresence>

          {/* Divider */}
          <div className="w-px h-7 bg-border/60 shrink-0" />

          {/* Model selector */}
          <div className="flex items-center gap-3 flex-1 min-w-0">
            {downloadedModels.length > 0 ? (
              <Select
                options={downloadedModels}
                value={store.model}
                onChange={handleModelChange}
                className="w-44"
                label="Model"
                disabled={store.modelLoading}
              />
            ) : (
              <div className="w-44">
                <label className="text-xs font-medium text-muted-foreground mb-1 block">Model</label>
                <div className="text-xs text-muted-foreground/60 italic px-1">No models downloaded</div>
              </div>
            )}
            <Select
              options={languageOptions}
              value={store.language || 'auto'}
              onChange={handleLanguageChange}
              className="w-36"
              label="Language"
            />
          </div>

          {/* Loading indicator */}
          {store.modelLoading && (
            <div className="flex items-center gap-1.5 text-xs text-accent shrink-0">
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
              <span>Loading...</span>
            </div>
          )}

          {/* Model note */}
          {store.modelNote && !store.modelLoading && (
            <span className="text-[10px] text-muted-foreground/60 truncate max-w-32 shrink-0" title={store.modelNote}>
              {store.modelNote}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}
