import React, { createContext, useContext, useState, useRef, useEffect, useCallback } from 'react';
import { AlertTriangle, Info, Trash2, LogOut, X } from 'lucide-react';
import { Button } from './Button';
import { cn } from '@/lib/utils';

export interface ConfirmOptions {
  title: string;
  description?: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'danger' | 'warning' | 'primary';
  iconType?: 'trash' | 'alert' | 'logout' | 'info';
}

interface ConfirmContextType {
  confirm: (options: ConfirmOptions) => Promise<boolean>;
}

const ConfirmContext = createContext<ConfirmContextType | null>(null);

export const ConfirmProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [isOpen, setIsOpen] = useState(false);
  const [options, setOptions] = useState<ConfirmOptions>({ title: '' });
  const [resolver, setResolver] = useState<((val: boolean) => void) | null>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  const confirm = useCallback((opts: ConfirmOptions): Promise<boolean> => {
    return new Promise((resolve) => {
      setOptions(opts);
      setIsOpen(true);
      setResolver(() => resolve);
    });
  }, []);

  const handleClose = useCallback((result: boolean) => {
    setIsOpen(false);
    if (resolver) {
      resolver(result);
      setResolver(null);
    }
  }, [resolver]);

  // Handle ESC and autofocus on Cancel
  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        handleClose(false);
      }
    };

    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', handleKeyDown);

    // Focus cancel button by default for safety
    const timer = setTimeout(() => {
      cancelButtonRef.current?.focus();
    }, 50);

    return () => {
      document.body.style.overflow = '';
      window.removeEventListener('keydown', handleKeyDown);
      clearTimeout(timer);
    };
  }, [isOpen, handleClose]);

  const renderIcon = () => {
    const { variant = 'danger', iconType } = options;

    if (iconType === 'logout') {
      return (
        <div className="w-10 h-10 rounded-full bg-muted flex items-center justify-center text-foreground shrink-0 border border-border">
          <LogOut className="w-5 h-5" />
        </div>
      );
    }

    if (variant === 'danger' || iconType === 'trash') {
      return (
        <div className="w-10 h-10 rounded-full bg-destructive/10 flex items-center justify-center text-destructive shrink-0 border border-destructive/20">
          <Trash2 className="w-5 h-5" />
        </div>
      );
    }

    if (variant === 'warning') {
      return (
        <div className="w-10 h-10 rounded-full bg-amber-500/10 flex items-center justify-center text-amber-500 shrink-0 border border-amber-500/20">
          <AlertTriangle className="w-5 h-5" />
        </div>
      );
    }

    return (
      <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center text-primary shrink-0 border border-primary/20">
        <Info className="w-5 h-5" />
      </div>
    );
  };

  return (
    <ConfirmContext.Provider value={{ confirm }}>
      {children}

      {isOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 animate-in fade-in duration-150"
          onClick={() => handleClose(false)}
        >
          <div
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="confirm-dialog-title"
            aria-describedby={options.description ? 'confirm-dialog-description' : undefined}
            onClick={(e) => e.stopPropagation()}
            className="w-full max-w-md bg-card border border-border rounded-xl shadow-2xl overflow-hidden flex flex-col animate-in zoom-in-95 duration-150"
          >
            {/* Header + Content */}
            <div className="p-5 flex items-start gap-4">
              {renderIcon()}

              <div className="flex-1 min-w-0 pr-6">
                <h3 id="confirm-dialog-title" className="text-sm font-semibold text-foreground tracking-tight">
                  {options.title}
                </h3>
                {options.description && (
                  <p id="confirm-dialog-description" className="text-xs text-muted-foreground mt-1.5 leading-relaxed">
                    {options.description}
                  </p>
                )}
              </div>

              <button
                type="button"
                onClick={() => handleClose(false)}
                aria-label="Đóng hộp thoại"
                className="absolute top-4 right-4 p-1 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Action Buttons */}
            <div className="px-5 py-3.5 bg-muted/40 border-t border-border flex items-center justify-end gap-2.5">
              <Button
                ref={cancelButtonRef}
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleClose(false)}
              >
                {options.cancelText || 'Hủy bỏ'}
              </Button>
              <Button
                type="button"
                variant={options.variant === 'danger' ? 'danger' : 'primary'}
                size="sm"
                onClick={() => handleClose(true)}
                className={cn(
                  options.variant === 'danger' &&
                    'bg-red-600 hover:bg-red-700 text-white border-transparent'
                )}
              >
                {options.confirmText || 'Xác nhận'}
              </Button>
            </div>
          </div>
        </div>
      )}
    </ConfirmContext.Provider>
  );
};

export const useConfirm = () => {
  const context = useContext(ConfirmContext);
  if (!context) {
    throw new Error('useConfirm must be used within a ConfirmProvider');
  }
  return context;
};
