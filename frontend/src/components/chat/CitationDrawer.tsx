import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { repositoryService } from '@/services/repositoryService';
import type { CitationDto } from '@/types/api';
import {
  X,
  Copy,
  Check,
  Maximize2,
  Minimize2,
  FileCode,
  ExternalLink,
  Code2,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { toast } from 'sonner';
import { CopyButton } from '@/components/ui/CopyButton';

interface CitationDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  citation: CitationDto | null;
  repositoryId?: string;
  projectId?: string;
}

export const CitationDrawer: React.FC<CitationDrawerProps> = ({
  isOpen,
  onClose,
  citation,
  repositoryId,
  projectId,
}) => {
  const [copied, setCopied] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const navigate = useNavigate();

  const targetRepoId = citation?.repositoryId || repositoryId;

  const { data: fileData, isLoading, isError } = useQuery({
    queryKey: ['code-file', targetRepoId, citation?.filePath, citation?.startLine, citation?.endLine],
    queryFn: () =>
      repositoryService.readFile(
        targetRepoId!,
        citation!.filePath,
        citation!.startLine,
        citation!.endLine
      ),
    enabled: isOpen && !!targetRepoId && !!citation?.filePath,
    retry: false,
  });

  if (!isOpen || !citation) return null;

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    toast.success('Đã sao chép đoạn code.');
    setTimeout(() => setCopied(false), 2000);
  };

  const contentToDisplay = fileData?.content || citation.snippet;
  const lines = contentToDisplay ? contentToDisplay.split('\n') : [];
  const startLineNum = fileData?.start ?? citation.startLine;

  const handleOpenFullExplorer = () => {
    if (projectId) {
      navigate(
        `/projects/${projectId}/explorer?file=${encodeURIComponent(
          citation.filePath
        )}&lineStart=${citation.startLine}&lineEnd=${citation.endLine}`
      );
    }
  };

  return (
    <aside
      className={cn(
        'fixed top-14 bottom-0 right-0 bg-card border-l border-border z-30 flex flex-col shadow-xl transition-all duration-200',
        isFullscreen ? 'left-64' : 'w-full md:w-[600px] xl:w-[680px]'
      )}
    >
      {/* Header */}
      <div className="h-12 px-4 border-b border-border flex items-center justify-between bg-card shrink-0">
        <div className="flex items-center gap-2 overflow-hidden mr-2">
          <FileCode className="w-4 h-4 text-primary shrink-0" />
          <div className="flex items-center gap-2 font-mono text-xs text-foreground truncate">
            <span className="font-semibold text-primary">[{citation.refId}]</span>
            <span className="truncate">{citation.filePath}</span>
            <span className="text-muted-foreground text-[11px]">
              L{citation.startLine}–{citation.endLine}
            </span>
          </div>
        </div>

        <div className="flex items-center gap-1 shrink-0">
          {projectId && (
            <button
              onClick={handleOpenFullExplorer}
              aria-label="Open full file in Code Explorer"
              className="px-2 py-1 rounded text-xs text-muted-foreground hover:text-foreground hover:bg-muted flex items-center gap-1 transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px]"
              title="Open full file in Code Explorer"
            >
              <Code2 className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Explorer</span>
            </button>
          )}

          <CopyButton text={contentToDisplay} size="sm" notifyToast />
          <button
            onClick={() => setIsFullscreen(!isFullscreen)}
            aria-label={isFullscreen ? 'Collapse drawer' : 'Expand drawer to full width'}
            aria-pressed={isFullscreen}
            className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition-colors hidden sm:flex min-h-[28px] min-w-[28px] items-center justify-center focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
            title={isFullscreen ? 'Collapse' : 'Expand'}
          >
            {isFullscreen ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
          </button>
          <button
            onClick={onClose}
            aria-label="Close citation drawer"
            className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
            title="Close"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Code Viewer Body */}
      <div className="flex-1 overflow-auto bg-background p-4 font-mono text-xs">
        {isLoading ? (
          <div className="p-8 text-center text-muted-foreground">Loading code slice...</div>
        ) : (
          <div className="table w-full border-collapse">
            {lines.map((line, idx) => {
              const currentLineNumber = startLineNum + idx;
              const isTargetLine =
                currentLineNumber >= citation.startLine && currentLineNumber <= citation.endLine;

              return (
                <div
                  key={idx}
                  className={`table-row leading-5 ${
                    isTargetLine ? 'bg-primary/10' : 'hover:bg-muted/40'
                  }`}
                >
                  <span className="table-cell text-right pr-4 pl-2 select-none text-muted-foreground/60 w-12 border-r border-border/40">
                    {currentLineNumber}
                  </span>
                  <span className="table-cell pl-4 whitespace-pre text-foreground">
                    {line || ' '}
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Footer */}
      <div className="h-8 px-4 border-t border-border bg-card flex items-center justify-between text-[11px] font-mono text-muted-foreground shrink-0">
        <span>Lines {citation.startLine}–{citation.endLine}</span>
        {citation.symbol && (
          <span className="truncate max-w-[300px]">
            {citation.symbolKind || 'symbol'}: {citation.symbol}
          </span>
        )}
      </div>
    </aside>
  );
};
