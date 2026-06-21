import { motion } from 'framer-motion';
import * as React from 'react';
import { cn } from '../../lib/utils/cn';

export interface SwitchProps {
  checked?: boolean;
  onCheckedChange?: (checked: boolean) => void;
  onChange?: (checked: boolean) => void;
  disabled?: boolean;
  className?: string;
  id?: string;
}

const Switch = React.forwardRef<HTMLButtonElement, SwitchProps>(
  ({ checked = false, onCheckedChange, onChange, disabled = false, className, id, ...props }, ref) => {
    const handleChange = onCheckedChange ?? onChange;
    return (
      <button
        type="button"
        ref={ref}
        id={id}
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => {
          if (!disabled && handleChange) {
            handleChange(!checked);
          }
        }}
        className={cn(
          'relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
          checked ? 'bg-accent' : 'bg-muted-foreground/25',
          disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer',
          className,
        )}
        {...props}
      >
        <motion.span
          className="pointer-events-none block h-4 w-4 rounded-full bg-white shadow-sm"
          animate={{ x: checked ? 18 : 2 }}
          transition={{ type: 'spring', stiffness: 500, damping: 30 }}
        />
      </button>
    );
  },
);
Switch.displayName = 'Switch';

export { Switch };
