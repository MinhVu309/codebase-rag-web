import React from 'react';
import type { RepositoryDto } from '@/types/api';
import { Loader2, CheckCircle2, AlertCircle, Clock } from 'lucide-react';
import { cn } from '@/lib/utils';

interface IndexingPipelineStepperProps {
  repo: RepositoryDto;
  compact?: boolean;
}

export const IndexingPipelineStepper: React.FC<IndexingPipelineStepperProps> = ({
  repo,
  compact = false,
}) => {
  const isReady = repo.status === 'ready';
  const isFailed = repo.status === 'failed';
  const stages = ['pending', 'cloning', 'parsing', 'embedding', 'ready'];
  const currentStage = repo.stage || repo.status;
  const currentIdx = stages.indexOf(currentStage);

  const stageLabels: Record<string, string> = {
    pending: 'Pending',
    cloning: 'Cloning',
    parsing: 'Parsing',
    embedding: 'Embedding',
    ready: 'Ready',
  };

  const percent =
    repo.totalFiles > 0
      ? Math.min(100, Math.round((repo.filesDone / repo.totalFiles) * 100))
      : isReady
      ? 100
      : currentIdx >= 0
      ? Math.round((currentIdx / (stages.length - 1)) * 100)
      : 0;

  if (compact) {
    return (
      <div className="flex items-center gap-2 text-xs">
        <div className="flex items-center gap-1.5 font-mono">
          {isReady ? (
            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500 shrink-0" />
          ) : isFailed ? (
            <AlertCircle className="w-3.5 h-3.5 text-destructive shrink-0" />
          ) : (
            <Loader2 className="w-3.5 h-3.5 animate-spin text-amber-500 shrink-0" />
          )}
          <span className="capitalize font-medium text-foreground">
            {stageLabels[currentStage] || currentStage}
          </span>
          {repo.totalFiles > 0 && !isReady && (
            <span className="text-muted-foreground text-[11px]">
              ({repo.filesDone}/{repo.totalFiles} files)
            </span>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2.5 p-3.5 rounded-lg bg-card border border-border">
      {/* Top Status & File Stats */}
      <div className="flex items-center justify-between text-xs font-mono">
        <div className="flex items-center gap-2">
          {isReady ? (
            <span className="flex items-center gap-1.5 text-emerald-500 font-medium">
              <CheckCircle2 className="w-4 h-4" />
              <span>Ready for AI Chat</span>
            </span>
          ) : isFailed ? (
            <span className="flex items-center gap-1.5 text-destructive font-medium">
              <AlertCircle className="w-4 h-4" />
              <span>Indexing Failed: {repo.errorCode || 'Error'}</span>
            </span>
          ) : (
            <span className="flex items-center gap-1.5 text-amber-500 font-medium">
              <Loader2 className="w-4 h-4 animate-spin" />
              <span>Indexing: <strong className="capitalize">{stageLabels[currentStage] || currentStage}</strong></span>
            </span>
          )}
        </div>

        {repo.totalFiles > 0 && (
          <div className="text-[11px] text-muted-foreground">
            {repo.filesDone} / {repo.totalFiles} files ({percent}%)
          </div>
        )}
      </div>

      {/* Progress Bar */}
      <div className="w-full h-1.5 rounded-full bg-muted overflow-hidden">
        <div
          className={cn(
            'h-full transition-all duration-300 rounded-full',
            isReady ? 'bg-emerald-500' : isFailed ? 'bg-destructive' : 'bg-amber-500'
          )}
          style={{ width: `${percent}%` }}
        />
      </div>

      {/* Stepper Pills */}
      <div className="grid grid-cols-5 gap-1.5 pt-1">
        {stages.map((st, idx) => {
          const isPast = currentIdx > idx || isReady;
          const isCurrent = currentStage === st && !isReady && !isFailed;

          return (
            <div
              key={st}
              className={cn(
                'flex items-center justify-center gap-1 py-1 px-1.5 rounded text-[10px] font-mono capitalize transition-all select-none',
                isPast && 'bg-emerald-500/10 text-emerald-500 border border-emerald-500/20 font-medium',
                isCurrent && 'bg-amber-500/15 text-amber-500 border border-amber-500/30 font-semibold animate-pulse',
                !isPast && !isCurrent && 'bg-muted/60 text-muted-foreground border border-transparent'
              )}
            >
              {isPast ? (
                <CheckCircle2 className="w-2.5 h-2.5 shrink-0" />
              ) : isCurrent ? (
                <Loader2 className="w-2.5 h-2.5 animate-spin shrink-0" />
              ) : (
                <Clock className="w-2.5 h-2.5 shrink-0 opacity-40" />
              )}
              <span className="truncate">{stageLabels[st]}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
};
