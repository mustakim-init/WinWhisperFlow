import type { ReactNode } from 'react';

export function SettingSection({
  title,
  description,
  children,
}: {
  title?: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <div className="space-y-1">
      {title && <h3 className="text-lg font-semibold">{title}</h3>}
      {description && <p className="text-sm text-muted-foreground">{description}</p>}
      <div className={`${title || description ? 'pt-3' : ''} space-y-0 divide-y divide-border/60`}>
        {children}
      </div>
    </div>
  );
}

export function SettingRow({
  title,
  description,
  htmlFor,
  action,
  children,
}: {
  title: ReactNode;
  description?: string;
  htmlFor?: string;
  action?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <div className="py-3">
      <div className="flex items-center justify-between gap-8">
        <div className="min-w-0">
          {htmlFor ? (
            <label htmlFor={htmlFor} className="text-sm font-medium leading-none select-none cursor-pointer">
              {title}
            </label>
          ) : (
            <div className="text-sm font-medium leading-none">{title}</div>
          )}
          {description && <p className="text-sm text-muted-foreground mt-0.5">{description}</p>}
        </div>
        {action && <div className="shrink-0">{action}</div>}
      </div>
      {children && <div className="mt-3">{children}</div>}
    </div>
  );
}
