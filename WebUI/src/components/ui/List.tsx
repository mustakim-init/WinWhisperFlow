import React from 'react';

interface ListProps {
  children: React.ReactNode;
  className?: string;
}

export function List({ children, className = '' }: ListProps) {
  return <div className={`flex flex-col gap-1 ${className}`}>{children}</div>;
}

interface ListItemProps {
  children: React.ReactNode;
  active?: boolean;
  onClick?: () => void;
  className?: string;
}

export function ListItem({ children, active, onClick, className = '' }: ListItemProps) {
  return (
    <div
      onClick={onClick}
      className={`rounded-lg px-3 py-2.5 text-sm transition-colors cursor-pointer ${
        active ? 'bg-muted text-foreground' : 'text-muted-foreground hover:bg-muted/50 hover:text-foreground'
      } ${className}`}
    >
      {children}
    </div>
  );
}
