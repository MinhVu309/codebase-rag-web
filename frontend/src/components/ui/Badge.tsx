import React from 'react';
import { cn } from '@/lib/utils';
import type { IndexStatus } from '@/types/api';

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  status?: IndexStatus;
  isActive?: boolean;
}

export const Badge: React.FC<BadgeProps> = ({ status, isActive, className, children, ...props }) => {
  let statusStyles = 'bg-surface-container-highest text-on-surface-variant border-outline-variant/40';
  let indicatorColor = 'bg-outline';

  if (status === 'ready') {
    statusStyles = 'bg-secondary-container/20 text-secondary border-secondary/30';
    indicatorColor = 'bg-secondary';
  } else if (status === 'failed') {
    statusStyles = 'bg-error-container/20 text-error border-error/30';
    indicatorColor = 'bg-error';
  } else if (status === 'cloning' || status === 'parsing' || status === 'embedding') {
    statusStyles = 'bg-tertiary-container/20 text-tertiary border-tertiary/30';
    indicatorColor = 'bg-tertiary';
  } else if (status === 'pending') {
    statusStyles = 'bg-surface-container-high text-outline border-outline-variant/30';
    indicatorColor = 'bg-outline';
  }

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-mono font-medium border',
        statusStyles,
        className
      )}
      {...props}
    >
      {status && (
        <span className={cn('w-1.5 h-1.5 rounded-full shrink-0', indicatorColor, (status === 'cloning' || status === 'parsing' || status === 'embedding') && 'animate-pulse')} />
      )}
      {children || status}
      {isActive && (
        <span className="ml-1 text-[10px] uppercase font-bold tracking-wider text-primary bg-primary-container/20 px-1 rounded">
          Active
        </span>
      )}
    </span>
  );
};
