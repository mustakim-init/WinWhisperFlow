import * as RadixSlider from '@radix-ui/react-slider';
import { cn } from '../../lib/utils/cn';

interface SliderProps {
  value: number;
  min: number;
  max: number;
  step: number;
  onChange: (value: number) => void;
  disabled?: boolean;
  className?: string;
  formatValue?: (v: number) => string;
}

export function Slider({
  value, min, max, step, onChange, disabled, className, formatValue,
}: SliderProps) {
  return (
    <div className={cn('flex items-center gap-3', className)}>
      <RadixSlider.Root
        className="relative flex h-6 w-full touch-none items-center"
        value={[value]}
        min={min}
        max={max}
        step={step}
        onValueChange={([v]) => onChange(v)}
        disabled={disabled}
      >
        <RadixSlider.Track className="relative h-2 grow rounded-full bg-secondary/50">
          <RadixSlider.Range className="absolute h-full rounded-full bg-accent" />
        </RadixSlider.Track>
        <RadixSlider.Thumb className="block h-5 w-5 rounded-full border-2 border-accent bg-background shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50" />
      </RadixSlider.Root>
      <span className="min-w-[3ch] text-right text-xs tabular-nums text-muted-foreground">
        {formatValue ? formatValue(value) : value}
      </span>
    </div>
  );
}
