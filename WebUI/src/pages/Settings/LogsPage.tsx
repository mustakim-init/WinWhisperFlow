import React, { useRef, useEffect, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { useStore } from '../../hooks/useStore';
import { clearLogs } from '../../lib/store';

function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  });
}

function LogLine({ entry }: { entry: { id: number; timestamp: number; message: string } }) {
  return (
    <div className="flex gap-3 font-mono text-xs leading-5 hover:bg-white/[0.02]">
      <span className="text-muted-foreground/40 select-none shrink-0 w-20">
        {formatTime(entry.timestamp)}
      </span>
      <span className="whitespace-pre-wrap break-all text-muted-foreground/80">
        {entry.message}
      </span>
    </div>
  );
}

export function LogsPage() {
  const store = useStore();
  const containerRef = useRef<HTMLDivElement>(null);
  const [autoScroll, setAutoScroll] = useState(true);

  useEffect(() => {
    if (autoScroll && containerRef.current) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight;
    }
  }, [store.logs.length, autoScroll]);

  const handleScroll = () => {
    const el = containerRef.current;
    if (!el) return;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
    setAutoScroll(atBottom);
  };

  const scrollToBottom = () => {
    setAutoScroll(true);
    containerRef.current?.scrollTo({ top: containerRef.current.scrollHeight, behavior: 'smooth' });
  };

  return (
    <div className="flex flex-col h-full min-h-0">
      <div className="flex items-center justify-between mb-3 shrink-0">
        <div>
          <h3 className="text-sm font-medium text-foreground">Activity Log</h3>
          <p className="text-sm text-muted-foreground">{store.logs.length} lines</p>
        </div>
        <div className="flex items-center gap-2">
          {!autoScroll && (
            <Button
              variant="outline"
              size="sm"
              onClick={scrollToBottom}
            >
              <ChevronDown size={14} /> Scroll
            </Button>
          )}
          <Button variant="outline" size="sm" onClick={clearLogs}>Clear</Button>
        </div>
      </div>

      <div className="relative flex-1 min-h-0">
        <div
          ref={containerRef}
          onScroll={handleScroll}
          className="absolute inset-0 overflow-y-auto rounded-lg border border-border/60 bg-black/20 p-3 font-mono text-xs leading-5"
        >
          {store.logs.length === 0 ? (
            <div className="text-muted-foreground/40">
              No log entries yet.
            </div>
          ) : (
            store.logs.map((entry) => <LogLine key={entry.id} entry={entry} />)
          )}
        </div>
      </div>
    </div>
  );
}
