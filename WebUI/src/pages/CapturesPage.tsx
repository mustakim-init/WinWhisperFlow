import { AnimatePresence, motion } from 'framer-motion';
import { Copy, FileText, Mic, Smartphone, Trash2 } from 'lucide-react';
import React, { useMemo, useState } from 'react';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { send } from '../bridge/ipc';
import { useStore } from '../hooks/useStore';
import { removeHistory } from '../lib/store';
import {
  ListPane,
  ListPaneHeader,
  ListPaneScroll,
  ListPaneSearch,
  ListPaneTitle,
  ListPaneTitleRow,
} from '../components/ListPane';
import { cn } from '../lib/utils/cn';

const sourceIcon: Record<string, React.ReactNode> = {
  mic: <Mic size={10} />,
  phone: <Smartphone size={10} />,
  file: <FileText size={10} />,
};

const sourceColor: Record<string, string> = {
  mic: 'bg-accent/10 text-accent border-accent/20',
  phone: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
  file: 'bg-blue-500/10 text-blue-400 border-blue-500/20',
};

export function CapturesPage() {
  const store = useStore();
  const [search, setSearch] = useState('');
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);

  const filtered = useMemo(
    () => {
      const list = store.history || [];
      return search
        ? list.filter((e) => e.text.toLowerCase().includes(search.toLowerCase()))
        : list;
    },
    [store.history, search],
  );

  // Default selection to first item
  const selected = useMemo(() => {
    if (filtered.length === 0) return null;
    if (selectedIndex === null || selectedIndex >= filtered.length) {
      return filtered[0];
    }
    return filtered[selectedIndex];
  }, [filtered, selectedIndex]);

  const activeIndex = useMemo(() => {
    if (!selected) return null;
    return filtered.indexOf(selected);
  }, [filtered, selected]);

  const handleCopy = (text: string) => send({ type: 'copy_text', text });

  const handleDelete = () => {
    if (selected) {
      const globalIndex = store.history.indexOf(selected);
      if (globalIndex !== -1) removeHistory(globalIndex);
      send({ type: 'delete_history_entry', ts: selected.ts, text: selected.text });
      setSelectedIndex(null);
    }
  };

  return (
    <div className="h-full flex gap-0 overflow-hidden -mx-8">
      {/* Left List Pane */}
      <div className="w-[340px] shrink-0">
        <ListPane>
          <ListPaneHeader>
            <ListPaneTitleRow className="pt-6">
              <ListPaneTitle className="px-0">Captures</ListPaneTitle>
              <Badge variant="outline" className="text-[10px] ml-2 bg-accent/10 border-accent/30 text-accent font-medium">
                History
              </Badge>
            </ListPaneTitleRow>
            <ListPaneSearch
              value={search}
              onChange={setSearch}
              placeholder="Search captures..."
            />
          </ListPaneHeader>

          <ListPaneScroll className="px-4 pb-6 space-y-1">
            <AnimatePresence mode="popLayout">
              {filtered.length === 0 ? (
                <div className="px-4 py-12 text-center text-sm text-muted-foreground italic">
                  {search ? 'No matches found.' : 'No captures yet.'}
                </div>
              ) : (
                filtered.map((entry, idx) => {
                  const isActive = activeIndex === idx;
                  return (
                    <button
                      key={entry.timestamp + entry.text.substring(0, 15) + idx}
                      onClick={() => setSelectedIndex(idx)}
                      className={cn(
                        'w-full text-left p-3.5 rounded-lg transition-colors block border border-transparent',
                        isActive
                          ? 'bg-muted/70 border-border shadow-sm'
                          : 'hover:bg-muted/30'
                      )}
                    >
                      <div className="flex items-center gap-2 mb-1.5">
                        <span className="text-[11px] text-muted-foreground font-medium">
                          {entry.timestamp}
                        </span>
                      </div>
                      <p className="text-[13px] text-foreground/90 line-clamp-2 leading-snug mb-2 select-none">
                        {entry.text}
                      </p>
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <Badge variant="outline" className={cn('text-[10px] gap-1 px-1.5 py-0.5', sourceColor[entry.source ?? 'mic'])}>
                          {sourceIcon[entry.source ?? 'mic']}
                          {entry.action}
                        </Badge>
                      </div>
                    </button>
                  );
                })
              )}
            </AnimatePresence>
          </ListPaneScroll>
        </ListPane>
      </div>

      {/* Right Detail Pane */}
      <div className="flex-1 flex flex-col relative overflow-hidden min-w-0">
        <div className="absolute top-0 left-0 right-0 h-20 bg-gradient-to-b from-background to-transparent z-10 pointer-events-none" />

        {/* Top Header Actions */}
        <div className="absolute top-0 left-0 right-0 z-20 px-8">
          <div className="flex items-center justify-between py-5">
            {selected ? (
              <div className="flex items-center gap-2">
                <Badge variant="outline" className={cn('text-[11px] gap-1 px-2 py-0.5', sourceColor[selected.source ?? 'mic'])}>
                  {sourceIcon[selected.source ?? 'mic']}
                  {selected.action}
                </Badge>
                <span className="text-xs text-muted-foreground">{selected.timestamp}</span>
              </div>
            ) : <div />}
          </div>
        </div>

        {/* Selected Capture Detail Content */}
        {selected ? (
          <div className="flex-1 overflow-y-auto pt-20 px-8 pb-8 flex flex-col gap-6">
            <div className="flex-1 rounded-xl border border-border bg-muted/10 flex flex-col min-h-[300px]">
              <textarea
                readOnly
                value={selected.text}
                className="flex-1 text-[15px] leading-relaxed border-0 bg-transparent resize-none focus:outline-none p-6 text-foreground font-sans select-text"
              />
            </div>

            <div className="flex items-center gap-2 shrink-0">
              <Button
                variant="outline"
                size="sm"
                onClick={() => handleCopy(selected.text)}
                className="gap-1.5"
              >
                <Copy size={14} /> Copy
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={handleDelete}
                className="gap-1.5"
              >
                <Trash2 size={14} /> Delete
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex-1 flex items-center justify-center text-sm text-muted-foreground italic select-none">
            Select a capture to view details
          </div>
        )}
      </div>
    </div>
  );
}
