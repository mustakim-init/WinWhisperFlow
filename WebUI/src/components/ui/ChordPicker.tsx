import { useEffect, useRef, useState } from 'react';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from './Dialog';
import { Badge } from './Badge';
import { Button } from './Button';
import { canonicalKeyFromEvent, displayLabelForKey, modifierSideHint, sortChordKeys, defaultChordKeys } from '../../lib/utils/keyCodes';

interface ChordPickerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChord: (keys: string[]) => void;
  initialKeys?: string[];
}

export function ChordPicker({ open, onOpenChange, onChord, initialKeys }: ChordPickerProps) {
  const [capturedKeys, setCapturedKeys] = useState<Set<string>>(new Set(initialKeys ?? defaultChordKeys()));
  const [isListening, setIsListening] = useState(false);
  const surfaceRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (open) {
      setCapturedKeys(new Set(initialKeys ?? defaultChordKeys()));
      setIsListening(true);
      setTimeout(() => surfaceRef.current?.focus(), 50);
    }
  }, [open, initialKeys]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    e.preventDefault();
    e.stopPropagation();

    if (e.key === 'Escape') {
      onOpenChange(false);
      return;
    }
    if (e.key === 'Tab') {
      return;
    }

    const canonical = canonicalKeyFromEvent(e.nativeEvent);
    if (!canonical) return;

    setCapturedKeys((prev) => new Set(prev).add(canonical));
  };

  const handleKeyUp = (e: React.KeyboardEvent) => {
    const canonical = canonicalKeyFromEvent(e.nativeEvent);
    if (!canonical) return;

    setCapturedKeys((prev) => {
      const next = new Set(prev);
      next.delete(canonical);
      return next;
    });
  };

  const chord = sortChordKeys(Array.from(capturedKeys));
  const hasMeaningfulChord = chord.some((k) => !k.startsWith('Control') && !k.startsWith('Shift') && !k.startsWith('Meta') && k !== 'Alt' && k !== 'AltGr');

  const handleSave = () => {
    if (chord.length === 0) return;
    onChord(chord);
    onOpenChange(false);
  };

  const handleReset = () => {
    const defaults = defaultChordKeys();
    setCapturedKeys(new Set(defaults));
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Record keyboard shortcut</DialogTitle>
          <DialogDescription>
            Press the key combination you want to use
          </DialogDescription>
        </DialogHeader>

        <div
          ref={surfaceRef}
          tabIndex={0}
          className="relative flex items-center justify-center min-h-24 rounded-xl border-2 border-dashed border-muted-foreground/30 bg-muted/10 focus-visible:border-accent focus-visible:outline-none transition-colors"
          onKeyDown={handleKeyDown}
          onKeyUp={handleKeyUp}
          onBlur={() => setIsListening(false)}
          onFocus={() => setIsListening(true)}
        >
          {chord.length === 0 ? (
            <span className="text-sm text-muted-foreground italic">Press keys...</span>
          ) : (
            <div className="flex items-center gap-1.5 flex-wrap justify-center px-4">
              {chord.map((key) => (
                <span key={key} className="relative">
                  <Badge variant="outline" className="text-xs font-mono px-2 py-1">
                    {displayLabelForKey(key)}
                  </Badge>
                  {modifierSideHint(key) && (
                    <span className="absolute -top-1 -right-1 text-[9px] font-bold text-accent bg-background rounded-full w-3.5 h-3.5 flex items-center justify-center border border-border">
                      {modifierSideHint(key)}
                    </span>
                  )}
                </span>
              ))}
            </div>
          )}

          {isListening && (
            <span className="absolute bottom-2 right-3 text-[10px] text-accent animate-pulse">
              Listening
            </span>
          )}
        </div>

        {!hasMeaningfulChord && chord.length > 0 && (
          <p className="text-xs text-amber-400 text-center">
            Add at least one non-modifier key (letter, number, or F-key)
          </p>
        )}

        <div className="flex items-center justify-between gap-3">
          <Button variant="ghost" size="sm" onClick={handleReset}>
            Reset default
          </Button>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button size="sm" onClick={handleSave} disabled={!hasMeaningfulChord || chord.length === 0}>
              Save
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
