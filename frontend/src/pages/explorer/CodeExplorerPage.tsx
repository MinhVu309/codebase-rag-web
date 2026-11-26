import React, { useState } from 'react';
import { useParams, useSearchParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { projectService } from '@/services/projectService';
import { repositoryService } from '@/services/repositoryService';
import { chatService } from '@/services/chatService';
import { Header } from '@/components/layout/Header';
import {
  FileCode,
  Search,
  Copy,
  Check,
  WrapText,
  Maximize2,
  Minimize2,
  ExternalLink,
  MessageSquareCode,
  GitFork,
  GitBranch,
  Loader2,
  X,
  FileText,
} from 'lucide-react';
import { toast } from 'sonner';
import { CopyButton } from '@/components/ui/CopyButton';

export const CodeExplorerPage: React.FC = () => {
  const { projectId } = useParams<{ projectId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();

  // Query params
  const initialFilePath = searchParams.get('file') || 'src/Program.cs';
  const initialLineStart = searchParams.get('lineStart') ? parseInt(searchParams.get('lineStart')!, 10) : 1;
  const initialLineEnd = searchParams.get('lineEnd') ? parseInt(searchParams.get('lineEnd')!, 10) : 25;
  const initialRepoId = searchParams.get('repoId');

  const [selectedFile, setSelectedFile] = useState(initialFilePath);
  const [lineStart, setLineStart] = useState(initialLineStart);
  const [lineEnd, setLineEnd] = useState(initialLineEnd);
  const [fileFilter, setFileFilter] = useState('');
  const [copied, setCopied] = useState(false);
  const [isWrap, setIsWrap] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  // 1. Fetch Project Details
  const { data: project } = useQuery({
    queryKey: ['project', projectId],
    queryFn: () => projectService.get(projectId!),
    enabled: !!projectId,
  });

  const activeRepo =
    project?.repositories.find((r) => (initialRepoId ? r.id === initialRepoId : r.isActive)) ||
    project?.repositories[0];
  const getRepoName = (url?: string) => (url ? url.replace('https://github.com/', '') : 'Repository');

  // 2. Fetch Conversations to harvest citations
  const { data: conversations = [] } = useQuery({
    queryKey: ['conversations', projectId],
    queryFn: () => chatService.listConversations(projectId!),
    enabled: !!projectId,
  });

  // Default known files list
  const defaultKnownFiles = [
    'src/Program.cs',
    'src/Controllers/AuthController.cs',
    'src/Services/RagService.cs',
    'src/Middleware/JwtAuthMiddleware.cs',
    'README.md',
  ];

  const handleSelectFile = (path: string, start = 1, end = 30) => {
    setSelectedFile(path);
    setLineStart(start);
    setLineEnd(end);
    setSearchParams({
      file: path,
      lineStart: String(start),
      lineEnd: String(end),
      ...(activeRepo ? { repoId: activeRepo.id } : {}),
    });
  };

  // 3. Read File Content
  const {
    data: fileData,
    isLoading: isFileLoading,
  } = useQuery({
    queryKey: ['code-file', activeRepo?.id, selectedFile, lineStart, lineEnd],
    queryFn: () =>
      repositoryService.readFile(activeRepo!.id, selectedFile, lineStart > 1 ? lineStart - 10 : 1, lineEnd + 30),
    enabled: !!activeRepo?.id && !!selectedFile,
    retry: false,
  });

  const content = fileData?.content || `// File: ${selectedFile}\n// Content preview will appear once indexed or when selected from citations.`;
  const lines = content.split('\n');
  const startNum = fileData?.start ?? Math.max(1, lineStart - 10);

  const handleCopyCode = () => {
    navigator.clipboard.writeText(content);
    setCopied(true);
    toast.success('Đã sao chép mã nguồn.');
    setTimeout(() => setCopied(false), 2000);
  };

  const fileExtension = selectedFile.split('.').pop()?.toUpperCase() || 'CODE';

  return (
    <>
      <Header
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: project?.name || 'Project', href: `/projects/${projectId}` },
          { label: 'Code Explorer' },
        ]}
      />

      <div id="main-content" className="pt-14 flex flex-col h-[calc(100vh-3.5rem)] overflow-hidden bg-background text-foreground select-none">
        {/* Sub-Header Toolbar */}
        <div className="h-10 bg-card px-4 flex items-center justify-between shrink-0 border-b border-border text-xs">
          <div className="flex items-center gap-2">
            <span className="text-muted-foreground font-mono">{activeRepo ? getRepoName(activeRepo.repoUrl) : 'repository'}</span>
            <span className="text-muted-foreground/60">/</span>
            <span className="font-mono font-medium text-foreground truncate max-w-[240px]">{selectedFile}</span>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => navigate(`/projects/${projectId}/chat`)}
              className="px-2.5 py-1 rounded bg-accent hover:bg-accent/80 text-accent-foreground text-xs font-medium transition-colors flex items-center gap-1.5"
            >
              <MessageSquareCode className="w-3.5 h-3.5" />
              <span>Back to Chat</span>
            </button>
          </div>
        </div>

        {/* 3-Pane Technical Grid Canvas */}
        <div className="flex-1 flex overflow-hidden w-full relative">
          {/* PANE 1: Left File Tree & Explorer (~260px) */}
          <aside className="w-64 bg-card shrink-0 flex flex-col border-r border-border z-20">
            {/* Repo & Branch Header */}
            <div className="p-3 border-b border-border bg-card">
              <div className="flex items-center justify-between mb-1">
                <div className="flex items-center gap-1.5 min-w-0">
                  <GitFork className="w-3.5 h-3.5 text-muted-foreground shrink-0" />
                  <span className="font-mono text-xs font-medium text-foreground truncate">
                    {activeRepo ? getRepoName(activeRepo.repoUrl) : 'Codebase'}
                  </span>
                </div>
                <span className="px-1.5 py-0.2 rounded bg-muted text-muted-foreground font-mono text-[10px]">
                  {fileExtension}
                </span>
              </div>
              <div className="flex items-center gap-1 text-muted-foreground font-mono text-[11px]">
                <GitBranch className="w-3 h-3" />
                <span>{activeRepo?.ref || 'main'}</span>
              </div>
            </div>

            {/* Quick File Filter */}
            <div className="p-2 border-b border-border">
              <div className="relative flex items-center">
                <Search className="w-3 h-3 absolute left-2 text-muted-foreground pointer-events-none" />
                <input
                  type="text"
                  placeholder="Filter files..."
                  value={fileFilter}
                  onChange={(e) => setFileFilter(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && fileFilter.trim()) {
                      handleSelectFile(fileFilter.trim());
                    }
                  }}
                  className="w-full pl-6 pr-2 py-1 rounded bg-background border border-border text-foreground font-mono text-xs placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-primary"
                />
              </div>
            </div>

            {/* Tree Navigation */}
            <div className="flex-1 overflow-y-auto py-2 font-mono text-xs">
              <div className="px-3 py-1 text-[10px] uppercase tracking-wider text-muted-foreground font-medium">
                Indexed Files
              </div>

              {defaultKnownFiles
                .filter((f) => f.toLowerCase().includes(fileFilter.toLowerCase()))
                .map((path) => {
                  const isSelected = selectedFile === path;
                  return (
                    <div
                      key={path}
                      onClick={() => handleSelectFile(path, 10, 35)}
                      className={`flex items-center justify-between px-3 py-1.5 cursor-pointer transition-colors ${
                        isSelected
                          ? 'bg-accent text-accent-foreground font-medium'
                          : 'hover:bg-muted/60 text-muted-foreground hover:text-foreground'
                      }`}
                    >
                      <div className="flex items-center gap-2 truncate">
                        <FileCode className="w-3.5 h-3.5 shrink-0 text-muted-foreground" />
                        <span className="truncate">{path}</span>
                      </div>
                    </div>
                  );
                })}
            </div>
          </aside>

          {/* PANE 2: Center Code Viewer */}
          <section className="flex-1 flex flex-col min-w-0 bg-background overflow-hidden">
            {/* Code Header Toolbar */}
            <div className="h-10 bg-card px-4 flex items-center justify-between border-b border-border shrink-0 text-xs">
              <div className="flex items-center gap-2 min-w-0 font-mono text-muted-foreground">
                <span className="text-foreground font-medium truncate">{selectedFile}</span>
                <span>•</span>
                <span>{lines.length} lines</span>
              </div>

              {/* Action Buttons */}
              <div className="flex items-center gap-1.5 shrink-0">
                <span className="px-2 py-0.5 rounded bg-muted text-foreground font-mono text-[11px]">
                  Lines {lineStart}–{lineEnd}
                </span>

                <CopyButton text={content} size="sm" notifyToast />
                <button
                  type="button"
                  onClick={() => setIsWrap(!isWrap)}
                  aria-label="Toggle word wrap"
                  aria-pressed={isWrap}
                  className={`p-1 rounded transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center ${
                    isWrap ? 'bg-accent text-accent-foreground' : 'text-muted-foreground hover:text-foreground hover:bg-muted'
                  }`}
                  title="Toggle Wrap"
                >
                  <WrapText className="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => setIsFullscreen(!isFullscreen)}
                  aria-label="Toggle fullscreen view"
                  aria-pressed={isFullscreen}
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
                  title="Fullscreen"
                >
                  {isFullscreen ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
                </button>
              </div>
            </div>

            {/* Code Buffer Area */}
            <div className="flex-1 overflow-auto relative font-mono text-xs leading-[22px] flex">
              {isFileLoading ? (
                <div className="flex-1 flex items-center justify-center text-muted-foreground gap-2">
                  <Loader2 className="w-4 h-4 animate-spin text-foreground" />
                  <span>Loading file content...</span>
                </div>
              ) : (
                <>
                  {/* Line Numbers Column */}
                  <div className="w-12 bg-muted/20 py-3 select-none shrink-0 border-r border-border/40 flex flex-col text-right pr-3 font-mono text-muted-foreground/50">
                    {lines.map((_, idx) => {
                      const curLine = startNum + idx;
                      const isCited = curLine >= lineStart && curLine <= lineEnd;
                      return (
                        <div
                          key={idx}
                          className={`h-[22px] ${isCited ? 'text-foreground font-bold' : ''}`}
                        >
                          {curLine}
                        </div>
                      );
                    })}
                  </div>

                  {/* Code Text Stream */}
                  <div
                    className={`flex-1 py-3 px-4 overflow-x-auto min-w-0 ${
                      isWrap ? 'whitespace-pre-wrap' : 'whitespace-pre'
                    }`}
                  >
                    {lines.map((line, idx) => {
                      const curLine = startNum + idx;
                      const isCited = curLine >= lineStart && curLine <= lineEnd;
                      return (
                        <div
                          key={idx}
                          className={`h-[22px] px-2 rounded transition-colors ${
                            isCited
                              ? 'bg-primary/10 text-foreground font-medium'
                              : 'text-muted-foreground hover:bg-muted/40'
                          }`}
                        >
                          {line || ' '}
                        </div>
                      );
                    })}
                  </div>
                </>
              )}
            </div>
          </section>

          {/* PANE 3: Right Citation Details */}
          <aside className="w-72 bg-card shrink-0 flex flex-col border-l border-border z-20 overflow-y-auto hidden lg:flex p-4 gap-4 text-xs">
            <div>
              <span className="text-[10px] text-muted-foreground uppercase font-medium tracking-wider">File Metadata</span>
              <div className="font-mono text-xs text-foreground break-all bg-background p-2 rounded border border-border mt-1">
                {selectedFile}
              </div>
            </div>

            <div className="grid grid-cols-2 gap-2 text-xs font-mono">
              <div className="p-2 rounded bg-background border border-border">
                <span className="text-[10px] text-muted-foreground">Range</span>
                <div className="font-semibold text-foreground mt-0.5">L{lineStart}–L{lineEnd}</div>
              </div>
              <div className="p-2 rounded bg-background border border-border">
                <span className="text-[10px] text-muted-foreground">Language</span>
                <div className="font-semibold text-foreground mt-0.5">{fileExtension}</div>
              </div>
            </div>

            <div className="pt-2 border-t border-border flex flex-col gap-2">
              <button
                type="button"
                onClick={() => navigate(`/projects/${projectId}/chat`)}
                className="w-full h-8 rounded-md bg-foreground text-background text-xs font-medium hover:bg-foreground/90 transition-all flex items-center justify-center gap-1.5 shadow-xs"
              >
                <MessageSquareCode className="w-3.5 h-3.5" />
                <span>Ask about this file in Chat</span>
              </button>
            </div>
          </aside>
        </div>
      </div>
    </>
  );
};
