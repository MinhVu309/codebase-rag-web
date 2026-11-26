import React, { useState } from 'react';
import { Copy, Check } from 'lucide-react';
import { cn } from '@/lib/utils';
import { toast } from 'sonner';

interface CopyButtonProps {
  text: string;
  className?: string;
  showLabel?: boolean;
  label?: string;
  successLabel?: string;
  size?: 'xs' | 'sm' | 'md';
  notifyToast?: boolean;
  title?: string;
}

export const CopyButton: React.FC<CopyButtonProps> = ({
  text,
  className,
  showLabel = false,
  label = 'Copy',
  successLabel = 'Copied!',
  size = 'sm',
  notifyToast = false,
  title = 'Copy to clipboard',
}) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async (e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      if (notifyToast) {
        toast.success('Đã sao chép vào bộ nhớ tạm.');
      }
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error('Không thể sao chép văn bản.');
    }
  };

  const iconSizes = {
    xs: 'w-3 h-3',
    sm: 'w-3.5 h-3.5',
    md: 'w-4 h-4',
  };

  return (
    <div className="relative inline-flex items-center">
      <button
        type="button"
        onClick={handleCopy}
        aria-label={copied ? successLabel : title}
        title={title}
        className={cn(
          'relative inline-flex items-center gap-1.5 px-2 py-1 rounded-md text-xs font-mono transition-all duration-200 select-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none',
          copied
            ? 'bg-emerald-500/10 text-emerald-500 border border-emerald-500/20'
            : 'text-muted-foreground hover:text-foreground hover:bg-muted/60 border border-transparent',
          className
        )}
      >
        <span className="relative flex items-center justify-center transition-transform duration-200">
          {copied ? (
            <Check className={cn(iconSizes[size], 'text-emerald-500 animate-in zoom-in-75 duration-200')} />
          ) : (
            <Copy className={cn(iconSizes[size], 'transition-transform hover:scale-105 duration-150')} />
          )}
        </span>

        {showLabel && (
          <span className={cn('text-[11px] font-medium transition-colors', copied && 'text-emerald-500 font-semibold')}>
            {copied ? successLabel : label}
          </span>
        )}
      </button>

      {/* Floating Pill Tooltip when copied (Lucian Element #008) */}
      {copied && !showLabel && (
        <span className="absolute -top-7 left-1/2 -translate-x-1/2 px-2 py-0.5 rounded bg-emerald-600 text-white text-[10px] font-mono font-medium shadow-md pointer-events-none animate-in fade-in slide-in-from-bottom-1 duration-150 z-30">
          Copied!
        </span>
      )}
    </div>
  );
};
