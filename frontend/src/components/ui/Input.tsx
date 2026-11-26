import React from 'react';
import { cn } from '@/lib/utils';

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  helperText?: string;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, label, error, helperText, leftIcon, rightIcon, id, ...props }, ref) => {
    const inputId = id || (label ? label.toLowerCase().replace(/\s+/g, '-') : undefined);
    const helperId = helperText && inputId ? `${inputId}-helper` : undefined;
    const errorId = error && inputId ? `${inputId}-error` : undefined;

    return (
      <div className="flex flex-col gap-1.5 w-full">
        {label && (
          <label htmlFor={inputId} className="text-xs font-medium text-foreground select-none">
            {label}
          </label>
        )}
        <div className="relative flex items-center w-full">
          {leftIcon && (
            <div className="absolute left-2.5 text-muted-foreground pointer-events-none flex items-center" aria-hidden="true">
              {leftIcon}
            </div>
          )}
          <input
            id={inputId}
            ref={ref}
            aria-invalid={!!error}
            aria-describedby={errorId || helperId}
            className={cn(
              'w-full bg-background border border-border rounded-md px-3 py-1.5 text-foreground placeholder:text-muted-foreground text-xs transition-colors',
              'focus-visible:outline-none focus-visible:border-primary focus-visible:ring-1 focus-visible:ring-primary',
              leftIcon && 'pl-8',
              rightIcon && 'pr-8',
              error && 'border-destructive focus-visible:border-destructive focus-visible:ring-destructive',
              className
            )}
            {...props}
          />
          {rightIcon && (
            <div className="absolute right-2.5 text-muted-foreground flex items-center" aria-hidden="true">
              {rightIcon}
            </div>
          )}
        </div>
        {error ? (
          <span id={errorId} role="alert" className="text-xs text-destructive font-medium">
            {error}
          </span>
        ) : helperText ? (
          <span id={helperId} className="text-xs text-muted-foreground">
            {helperText}
          </span>
        ) : null}
      </div>
    );
  }
);
Input.displayName = 'Input';
