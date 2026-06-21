import { AnimatePresence, motion } from 'framer-motion';
import { Copy, Loader2, Mic, Square } from 'lucide-react';
import React from 'react';
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

  const handleCopy = (text: string) => send({ type: 'copy_text', text });

  return (
    <div className="flex flex-col h-full min-h-0 relative max-w-3xl mx-auto w-full">
      {/* Gradient fade top */}
      <div className="absolute top-0 left-0 right-0 h-16 bg-gradient-to-b from-background to-transparent pointer-events-none z-10" />

      {/* Header */}
      <div className="shrink-0 pt-8 pb-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold">Transcribe</h1>
            <p className="text-sm text-muted-foreground mt-0.5">Record and transcribe speech in real-time</p>
          </div>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto pb-8 space-y-6 min-h-0 scrollbar-hide pt-2">
        {/* Model and language controls - Clean group, no double border cards */}
        <div className="rounded-xl border border-border bg-card p-4 flex items-center justify-between gap-4 flex-wrap">
          <div className="flex items-center gap-4 flex-wrap flex-1 min-w-0">
            {downloadedModels.length > 0 ? (
              <Select
                options={downloadedModels}
                value={store.model}
                onChange={handleModelChange}
                className="w-48"
                label="Model"
                disabled={store.modelLoading}
              />
            ) : (
              <div className="w-48">
                <label className="text-xs font-medium text-muted-foreground mb-1 block">Model</label>
                <div className="text-xs text-muted-foreground/60 italic px-1">No models downloaded</div>
              </div>
            )}
            <Select
              options={languageOptions}
              value={store.language || 'auto'}
              onChange={handleLanguageChange}
              className="w-40"
              label="Language"
            />
          </div>

          {store.modelLoading && (
            <div className="flex items-center gap-2 text-xs text-accent shrink-0">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span>Loading model...</span>
            </div>
          )}
        </div>

        {/* Record button and levels */}
        <div className="flex flex-col items-center justify-center py-6 gap-4 border border-border/50 rounded-xl bg-muted/10">
          <div className="relative">
            <motion.button
              onClick={handleToggle}
              disabled={store.modelLoading}
              className="relative w-24 h-24 rounded-full bg-accent text-accent-foreground shadow-[0_0_20px_hsl(var(--accent)/0.3)] flex items-center justify-center disabled:opacity-50 disabled:cursor-not-allowed hover:opacity-95 transition-opacity"
              whileTap={{ scale: 0.95 }}
            >
              {store.isListening ? (
                <Square className="h-8 w-8 fill-current" />
              ) : (
                <Mic className="h-8 w-8" />
              )}
              {store.isListening && (
                <motion.span
                  className="absolute inset-0 rounded-full bg-accent"
                  animate={{ scale: [1, 1.15, 1], opacity: [0.3, 0, 0.3] }}
                  transition={{ repeat: Infinity, duration: 1.5, ease: 'easeInOut' }}
                />
              )}
            </motion.button>
          </div>

          {/* Audio level meter */}
          {store.isListening && (
            <div className="w-48">
              <div className="h-1.5 w-full rounded-full bg-muted overflow-hidden">
                <motion.div
                  animate={{ width: `${Math.min(100, store.audioLevel * 100)}%` }}
                  transition={{ duration: 0.05 }}
                  className="h-full rounded-full bg-accent"
                />
              </div>
            </div>
          )}

          {store.modelNote && (
            <p className="text-xs text-muted-foreground/80 px-4 text-center max-w-md">{store.modelNote}</p>
          )}
        </div>

        {/* Transcription area */}
        <div className="space-y-4">
          <AnimatePresence mode="wait">
            {store.isListening || store.lastTranscript ? (
              <motion.div
                key="content"
                initial={{ opacity: 0, y: 5 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
                className="space-y-4"
              >
                <div className="rounded-xl border border-border bg-muted/15 overflow-hidden">
                  <div className="flex items-center justify-between px-6 py-3 border-b border-border bg-card/50">
                    <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                      {store.isListening ? 'Recording Live' : 'Transcription'}
                    </h2>
                    {(store.isListening ? store.partialMeta : store.lastMeta) ? (
                      <Badge variant="outline" className="text-[10px] bg-accent/5 border-accent/20 text-accent">
                        {store.isListening ? store.partialMeta : store.lastMeta}
                      </Badge>
                    ) : null}
                  </div>
                  
                  <div className="min-h-[220px] text-[15px] leading-relaxed whitespace-pre-wrap select-text p-6 text-foreground font-sans">
                    {store.isListening ? (
                      store.partialTranscript || (
                        <span className="text-muted-foreground italic">Transcription appears here...</span>
                      )
                    ) : (
                      store.lastTranscript || (
                        <span className="text-muted-foreground italic">Transcription appears here...</span>
                      )
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

                {store.lastTranscript && (
                  <div className="flex gap-2">
                    <Button variant="secondary" size="sm" onClick={() => handleCopy(store.lastTranscript)} className="gap-1.5">
                      <Copy size={14} /> Copy
                    </Button>
                  </div>
                )}
              </motion.div>
            ) : (
              <motion.div
                key="empty"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="flex items-center justify-center h-48 border border-dashed border-border/80 rounded-xl text-sm text-muted-foreground italic select-none"
              >
                Press the microphone button to start transcribing
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>
    </div>
  );
}
