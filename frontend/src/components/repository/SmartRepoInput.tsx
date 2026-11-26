import React, { useMemo } from 'react';
import { GitFork, Check, AlertCircle, Sparkles, ExternalLink, GitBranch } from 'lucide-react';
import { cn } from '@/lib/utils';

interface SmartRepoInputProps {
  value: string;
  onChange: (val: string) => void;
  selectedBranch: string;
  onSelectBranch: (branch: string) => void;
}

export const SmartRepoInput: React.FC<SmartRepoInputProps> = ({
  value,
  onChange,
  selectedBranch,
  onSelectBranch,
}) => {
  // Parse GitHub repo URL: https://github.com/:owner/:repo(.git)?(/)?
  const parsedRepo = useMemo(() => {
    if (!value.trim()) return null;

    const trimmed = value.trim();
    const regex = /^https:\/\/github\.com\/([a-zA-Z0-9_.-]+)\/([a-zA-Z0-9_.-]+?)(?:\.git|\/)?$/;
    const match = trimmed.match(regex);

    if (match) {
      return {
        isValid: true,
        owner: match[1],
        repoName: match[2],
        fullPath: `${match[1]}/${match[2]}`,
      };
    }

    return {
      isValid: false,
      owner: null,
      repoName: null,
      fullPath: null,
    };
  }, [value]);

  const handlePasteClipboard = async () => {
    try {
      const text = await navigator.clipboard.readText();
      if (text) {
        onChange(text.trim());
      }
    } catch {
      // ignore
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <label className="text-xs font-medium text-foreground" htmlFor="repo-url-input">
          GitHub Repository URL <span className="text-destructive">*</span>
        </label>
        {!value && (
          <button
            type="button"
            onClick={handlePasteClipboard}
            className="text-[11px] text-primary hover:underline flex items-center gap-1 font-mono"
          >
            <Sparkles className="w-2.5 h-2.5" />
            <span>Dán từ bộ nhớ tạm</span>
          </button>
        )}
      </div>

      <div className="relative flex items-center">
        <div className="absolute left-3 text-muted-foreground pointer-events-none flex items-center justify-center">
          <GitFork className="w-4 h-4" />
        </div>

        <input
          id="repo-url-input"
          type="url"
          required
          autoFocus
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder="https://github.com/organization/repository"
          className={cn(
            'w-full pl-9 pr-8 py-2 rounded-lg bg-muted text-foreground placeholder:text-muted-foreground text-xs font-mono border transition-all duration-150 focus-visible:outline-none focus-visible:ring-1',
            parsedRepo?.isValid
              ? 'border-emerald-500/50 focus-visible:ring-emerald-500'
              : value && !parsedRepo?.isValid
              ? 'border-amber-500/50 focus-visible:ring-amber-500'
              : 'border-border focus-visible:ring-primary'
          )}
        />

        {parsedRepo?.isValid && (
          <div className="absolute right-2.5 text-emerald-500">
            <Check className="w-4 h-4 animate-in zoom-in-50 duration-150" />
          </div>
        )}
      </div>

      {/* Realtime Live Preview Badge */}
      {parsedRepo?.isValid ? (
        <div className="p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex flex-col gap-2 animate-in fade-in slide-in-from-top-1 duration-150">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
              <span className="text-xs font-semibold text-emerald-400 font-mono">
                {parsedRepo.fullPath}
              </span>
            </div>
            <a
              href={`https://github.com/${parsedRepo.fullPath}`}
              target="_blank"
              rel="noreferrer"
              className="text-[11px] text-emerald-400/80 hover:text-emerald-300 flex items-center gap-1 font-mono"
            >
              <span>Xem trên GitHub</span>
              <ExternalLink className="w-2.5 h-2.5" />
            </a>
          </div>

          <div className="flex items-center gap-2 text-[11px] text-muted-foreground pt-1 border-t border-emerald-500/15">
            <span className="flex items-center gap-1">
              <GitBranch className="w-3 h-3 text-emerald-500" />
              <span>Gợi ý nhánh:</span>
            </span>
            <div className="flex items-center gap-1">
              {['main', 'master', 'develop'].map((b) => (
                <button
                  key={b}
                  type="button"
                  onClick={() => onSelectBranch(selectedBranch === b ? '' : b)}
                  className={cn(
                    'px-2 py-0.5 rounded text-[10px] font-mono transition-colors',
                    selectedBranch === b
                      ? 'bg-emerald-500 text-white font-semibold'
                      : 'bg-muted hover:bg-muted/80 text-foreground border border-border'
                  )}
                >
                  {b}
                </button>
              ))}
            </div>
          </div>
        </div>
      ) : value ? (
        <div className="flex items-center gap-1.5 text-[11px] text-amber-500 animate-in fade-in duration-150">
          <AlertCircle className="w-3 h-3 shrink-0" />
          <span>Định dạng hợp lệ: https://github.com/owner/repository</span>
        </div>
      ) : (
        <p className="text-[11px] text-muted-foreground">
          Chỉ chấp nhận GitHub repository công khai (ví dụ: <code className="text-foreground">https://github.com/pallets/flask</code>)
        </p>
      )}
    </div>
  );
};
