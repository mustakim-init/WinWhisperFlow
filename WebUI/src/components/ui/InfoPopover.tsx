import * as Popover from '@radix-ui/react-popover';
import { Info } from 'lucide-react';
import { cn } from '../../lib/utils/cn';

interface InfoPopoverProps {
  title: string;
  description: string;
  recommended: string;
  className?: string;
}

export function InfoPopover({ title, description, recommended, className }: InfoPopoverProps) {
  return (
    <Popover.Root>
      <Popover.Trigger asChild>
        <button
          type="button"
          className={cn(
            'inline-flex items-center justify-center rounded-full w-4 h-4 text-[10px]',
            'text-muted-foreground/60 hover:text-muted-foreground transition-colors',
            'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
            className,
          )}
          aria-label={`Info about ${title}`}
        >
          <Info className="w-3.5 h-3.5" />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          side="top"
          align="center"
          sideOffset={6}
          className={cn(
            'z-50 max-w-64 rounded-lg border border-border/60 bg-popover px-3 py-2.5',
            'text-popover-foreground shadow-md',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
            'data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95',
            'data-[side=bottom]:slide-in-from-top-2 data-[side=top]:slide-in-from-bottom-2',
          )}
        >
          <p className="text-xs leading-relaxed text-muted-foreground">{description}</p>
          <p className="text-xs font-medium mt-1.5 text-foreground/80">
            Recommended: {recommended}
          </p>
          <Popover.Arrow className="fill-border" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
