import { motion } from 'framer-motion';
import * as React from 'react';
import { cn } from '../../lib/utils/cn';

interface ProgressProps {
  value?: number;
  className?: string;
}

const Progress = React.forwardRef<HTMLDivElement, ProgressProps>(
  ({ className, value = 0, ...props }, ref) => (
    <div
      ref={ref}
      className={cn('relative h-2 w-full overflow-hidden rounded-full bg-secondary', className)}
      {...props}
    >
      <motion.div
        className="h-full rounded-full bg-accent"
        initial={{ width: '0%' }}
        animate={{ width: `${Math.min(value, 100)}%` }}
        transition={{ type: 'spring', stiffness: 100, damping: 20 }}
      />
    </div>
  ),
);
Progress.displayName = 'Progress';

export { Progress };
