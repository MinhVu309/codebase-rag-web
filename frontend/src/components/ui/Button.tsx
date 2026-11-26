import React from 'react';
import { cn } from '@/lib/utils';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  icon?: React.ReactNode;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', size = 'md', loading, disabled, icon, children, ...props }, ref) => {
    const baseStyles = 'inline-flex items-center justify-center font-medium rounded-md transition-all focus:outline-none focus:ring-1 focus:ring-primary disabled:opacity-50 disabled:pointer-events-none select-none active:scale-[0.98]';

    const variants = {
      primary: 'bg-foreground hover:bg-foreground/90 text-background shadow-xs font-medium',
      secondary: 'bg-muted hover:bg-muted/80 text-foreground border border-border',
      outline: 'bg-transparent border border-border hover:bg-muted text-foreground',
      ghost: 'bg-transparent hover:bg-muted text-muted-foreground hover:text-foreground',
      danger: 'bg-destructive/10 hover:bg-destructive/20 text-destructive border border-destructive/20 font-medium',
    };

    const sizes = {
      sm: 'px-2.5 py-1 text-xs gap-1.5 h-7',
      md: 'px-3.5 py-1.5 text-xs gap-2 h-8',
      lg: 'px-4 py-2 text-sm gap-2 h-9',
    };

    return (
      <button
        ref={ref}
        className={cn(baseStyles, variants[variant], sizes[size], className)}
        disabled={disabled || loading}
        {...props}
      >
        {loading ? (
          <span className="w-3.5 h-3.5 border-2 border-current border-t-transparent rounded-full animate-spin" />
        ) : icon ? (
          <span className="shrink-0">{icon}</span>
        ) : null}
        {children}
      </button>
    );
  }
);
Button.displayName = 'Button';
