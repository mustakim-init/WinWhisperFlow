import { AnimatePresence, motion } from 'framer-motion';
import { Check, Clock, Copy, FileText, Loader2, Mic, Music, RefreshCw, Square, Upload, X } from 'lucide-react';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { Progress } from '../components/ui/Progress';
import { Select } from '../components/ui/Select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../components/ui/Tabs';
import { send } from '../bridge/ipc';
import { useStore } from '../hooks/useStore';
import { setFileMusicMode, setFileTranscript, setMusicTranscript, setVoiceTranscript } from '../lib/store';

const languageOptions = [
  { label: 'Auto detect', value: 'auto' },
  { label: 'English', value: 'en' },
  { label: 'Bengali', value: 'bn' },
];

const stageLabels: Record<string, string> = {
  extracting: 'Extracting audio',
  separating: 'Separating vocals',
  transcribing: 'Transcribing',
  done: 'Done',
};

function formatTime(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function FileTranscribeProgress() {
  const store = useStore();

  return (
    <div className="flex-1 flex flex-col items-center justify-center gap-4 p-8">
      <div className="flex items-center gap-3">
        <Loader2 className="h-6 w-6 animate-spin text-accent" />
        <span className="text-base font-medium">{store.fileName}</span>
      </div>
      <div className="w-full max-w-xs space-y-2">
        <Progress value={store.fileProgress} className="h-2" />
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span>{stageLabels[store.fileStage] || store.fileStage}</span>
          <span>{Math.round(store.fileProgress)}%</span>
        </div>
      </div>
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Clock size={12} />
        <span>{formatTime(store.fileElapsed)}</span>
      </div>
      <Button
        variant="outline"
        size="sm"
        className="text-xs gap-1"
        onClick={() => send({ type: 'cancel_file_transcribe' })}
      >
        <X size={12} /> Cancel
      </Button>
    </div>
  );
}

function FileResultView({ musicMode }: { musicMode: boolean }) {
  const store = useStore();
  const [copied, setCopied] = useState(false);
  const storeField = musicMode ? store.musicTranscript : store.fileTranscript;
  const [editText, setEditText] = useState(() => storeField);
  const editRef = useRef(editText);
  editRef.current = editText;

  const handleCopy = () => {
    send({ type: 'copy_text', text: editText });
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  const handleNewFile = () => {
    setFileMusicMode(musicMode);
    send({ type: 'transcribe_file', musicMode });
  };

  useEffect(() => {
    return () => {
      const current = editRef.current;
      if (musicMode) {
        if (current !== store.musicTranscript) {
          setMusicTranscript(current, store.musicMeta);
        }
      } else {
        if (current !== store.fileTranscript) {
          setFileTranscript(current, store.fileMeta);
        }
      }
    };
  }, []);

  return (
    <div className="flex-1 min-h-0 flex flex-col">
      <div className="flex-1 min-h-0 rounded-xl border border-border bg-muted/15 overflow-hidden flex flex-col">
        <div className="flex items-center justify-between px-6 py-3 border-b border-border bg-card/50 shrink-0">
          <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
            {musicMode ? 'Music Transcription' : 'File Transcription'}
          </h2>
          {storeField && (
            <div className="flex items-center gap-2">
              <Button variant="ghost" size="sm" onClick={handleCopy} className="gap-1.5 h-7 text-xs text-muted-foreground hover:text-foreground">
                {copied ? <Check size={12} /> : <Copy size={12} />} {copied ? 'Copied!' : 'Copy'}
              </Button>
              <Button variant="ghost" size="sm" onClick={handleNewFile} className="gap-1.5 h-7 text-xs text-muted-foreground hover:text-foreground">
                <RefreshCw size={12} /> New
              </Button>
            </div>
          )}
        </div>
        <div className="flex-1 flex flex-col min-h-0 p-6">
          {storeField ? (
            <textarea
              value={editText}
              onChange={(e) => setEditText(e.target.value)}
              className="flex-1 w-full bg-transparent border-none outline-none resize-none text-[15px] leading-relaxed font-sans text-foreground"
            />
          ) : (
            <span className="text-muted-foreground italic">No transcription yet. Select a file to begin.</span>
          )}
        </div>
      </div>
    </div>
  );
}

function FileImportTab({ musicMode }: { musicMode: boolean }) {
  const store = useStore();
  const [dragOver, setDragOver] = useState(false);
  const hasResult = musicMode ? !!store.musicTranscript : !!store.fileTranscript;

  const demucsDownloaded = store.availableModels.find((m) => m.name === 'demucs-htdemucs')?.downloaded ?? false;

  const handleOpenFile = () => {
    setFileMusicMode(musicMode);
    send({ type: 'transcribe_file', musicMode });
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (!file) return;
    setFileMusicMode(musicMode);
    const reader = new FileReader();
    reader.onload = () => {
      const base64 = (reader.result as string)?.split(',')[1] ?? '';
      send({ type: 'transcribe_dropped_file', data: base64, name: file.name });
    };
    reader.readAsDataURL(file);
  }, [musicMode]);

  if (store.fileTranscribing) {
    return <FileTranscribeProgress />;
  }

  if (hasResult) {
    return <FileResultView musicMode={musicMode} />;
  }

  if (musicMode && !demucsDownloaded) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center border border-dashed border-border/80 rounded-xl text-sm text-muted-foreground select-none gap-4 p-8">
        <Music size={40} className="text-muted-foreground/30" />
        <span className="text-base font-medium">Demucs not installed</span>
        <span className="text-xs text-center max-w-xs">
          Music transcription requires Demucs for vocal separation.
          Go to the <span className="font-medium">Models</span> page and download &ldquo;htdemucs&rdquo; under Tools.
        </span>
      </div>
    );
  }

  return (
    <div
      className={`flex-1 flex flex-col items-center justify-center border border-dashed rounded-xl text-sm text-muted-foreground select-none gap-4 p-8 transition-colors ${
        dragOver ? 'border-accent bg-accent/5 ring-1 ring-accent' : 'border-border/80'
      }`}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      {musicMode ? <Music size={40} className="text-muted-foreground/30" /> : <FileText size={40} className="text-muted-foreground/30" />}
      <span className="text-base font-medium">
        {musicMode ? 'Transcribe music with vocals separation' : 'Transcribe speech from a file'}
      </span>
      <span className="text-xs text-muted-foreground/60">
        {musicMode
          ? 'Audio will be separated (vocals removed from instruments) then transcribed'
          : 'File will be transcribed directly without source separation'}
      </span>
      <Button onClick={handleOpenFile} className="gap-2 mt-2">
        <Upload size={16} />
        Select {musicMode ? 'Music' : 'Speech'} File
      </Button>
      <span className="text-[11px] text-muted-foreground/40">or drag and drop a file here</span>
    </div>
  );
}

function VoiceEmptyState() {
  return (
    <motion.div
      key="empty"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      className="flex-1 flex flex-col items-center justify-center border border-dashed border-border/80 rounded-xl text-sm text-muted-foreground select-none gap-3"
    >
      <Mic size={32} className="text-muted-foreground/30" />
      <span className="italic">Press the record button to start transcribing</span>
    </motion.div>
  );
}

function VoiceTranscriptView() {
  const store = useStore();
  const [copied, setCopied] = useState(false);
  const [editText, setEditText] = useState(() => store.voiceTranscript);
  const editRef = useRef(editText);
  const wasListening = useRef(store.isListening);
  editRef.current = editText;

  const metaText = store.isListening ? store.voicePartialMeta : store.voiceMeta;

  // Sync editText from store when a new transcription arrives after recording stops
  useEffect(() => {
    if (wasListening.current && !store.isListening) {
      setEditText(store.voiceTranscript);
    }
    wasListening.current = store.isListening;
  }, [store.isListening, store.voiceTranscript]);

  // Persist edits to store on unmount (tab switch)
  useEffect(() => {
    return () => {
      const current = editRef.current;
      if (current !== store.voiceTranscript) {
        setVoiceTranscript(current, store.voiceMeta);
      }
    };
  }, []);

  const handleCopy = () => {
    send({ type: 'copy_text', text: editText });
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  return (
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
            {store.isListening ? 'Recording Live' : 'Voice Transcription'}
          </h2>
          {store.voiceTranscript && !store.isListening && (
            <Button variant="ghost" size="sm" onClick={handleCopy} className="gap-1.5 h-7 text-xs text-muted-foreground hover:text-foreground">
              {copied ? <Check size={12} /> : <Copy size={12} />} {copied ? 'Copied!' : 'Copy'}
            </Button>
          )}
        </div>
        <div className="flex-1 flex flex-col min-h-0 p-6">
          {store.isListening ? (
            <div className="overflow-y-auto text-[15px] leading-relaxed whitespace-pre-wrap select-text text-foreground font-sans">
              {store.voicePartialTranscript || (
                <span className="text-muted-foreground italic">Transcription appears here...</span>
              )}
              <motion.span
                animate={{ opacity: [1, 0] }}
                transition={{ repeat: Infinity, duration: 0.8 }}
                className="inline-block w-0.5 h-4 bg-accent ml-1 align-middle"
              />
            </div>
          ) : (
            <textarea
              value={editText}
              onChange={(e) => setEditText(e.target.value)}
              className="flex-1 w-full bg-transparent border-none outline-none resize-none text-[15px] leading-relaxed font-sans text-foreground"
            />
          )}
        </div>
      </div>
      {metaText && (
        <Badge variant="outline" className="mt-2 self-start text-[10px] bg-accent/5 border-accent/20 text-accent">
          {metaText}
        </Badge>
      )}
    </motion.div>
  );
}

export function DictatePage() {
  const store = useStore();
  const [activeTab, setActiveTab] = useState('voice');
  const barSeeds = React.useMemo(() => [0.7, 0.9, 0.6, 1.0, 0.8], []);

  const downloadedModels = store.availableModels
    .filter((m) => m.downloaded && m.provider !== 'demucs')
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

  const showVoiceContent = store.isListening || !!store.voiceTranscript;

  const handleImportFile = () => {
    const musicMode = activeTab === 'music';
    setFileMusicMode(musicMode);
    send({ type: 'transcribe_file', musicMode });
  };

  return (
    <div className="flex flex-col h-full min-h-0 relative">
      {/* Title */}
      <h1 className="shrink-0 pt-8 pb-4 text-2xl font-bold">Transcribe</h1>

      {/* Tabs root fills remaining space */}
      <Tabs defaultValue="voice" onValueChange={setActiveTab} className="flex flex-col flex-1 min-h-0">
        <TabsList className="shrink-0">
          <TabsTrigger value="voice" className="flex items-center gap-1.5">
            <Mic size={14} /> Voice
          </TabsTrigger>
          <TabsTrigger value="music" className="flex items-center gap-1.5">
            <Music size={14} /> Music Import
          </TabsTrigger>
          <TabsTrigger value="speech" className="flex items-center gap-1.5">
            <FileText size={14} /> Speech Import
          </TabsTrigger>
        </TabsList>

        {/* Voice Tab */}
        <TabsContent value="voice" className="flex-1 min-h-0 flex flex-col mt-0 pt-4 pb-28">
          {showVoiceContent ? <VoiceTranscriptView /> : <VoiceEmptyState />}
        </TabsContent>

        {/* Music Import Tab */}
        <TabsContent value="music" className="flex-1 min-h-0 flex flex-col mt-0 pt-4 pb-28">
          <FileImportTab musicMode />
        </TabsContent>

        {/* Speech Import Tab */}
        <TabsContent value="speech" className="flex-1 min-h-0 flex flex-col mt-0 pt-4 pb-28">
          <FileImportTab musicMode={false} />
        </TabsContent>
      </Tabs>

      {/* Floating bottom control bar */}
      <div className="absolute bottom-0 left-0 right-0 z-20 pb-4 px-2 pointer-events-none">
        <div className="pointer-events-auto mx-auto max-w-2xl rounded-2xl border border-border bg-card/95 backdrop-blur-md shadow-lg px-4 py-3 flex items-center gap-3">
          {/* Action button — record for voice, import for file tabs */}
          {activeTab === 'voice' ? (
            <>
              <motion.button
                onClick={handleToggle}
                disabled={store.modelLoading || store.fileTranscribing}
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
            </>
          ) : (
            <Button
              onClick={handleImportFile}
              disabled={store.modelLoading || store.fileTranscribing}
              className="shrink-0 gap-2"
            >
              <Upload size={16} />
              Select {activeTab === 'music' ? 'Music' : 'Speech'} File
            </Button>
          )}

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
