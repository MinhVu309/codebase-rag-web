import React, { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { projectService } from '@/services/projectService';
import { repositoryService } from '@/services/repositoryService';
import { Header } from '@/components/layout/Header';
import { getErrorMessage, formatTimeAgo, formatDate } from '@/lib/utils';
import {
  GitFork,
  Plus,
  MessageSquareCode,
  Code2,
  Trash2,
  Edit2,
  GitBranch,
  ExternalLink,
  Loader2,
  AlertCircle,
  Check,
  X,
  Clock,
  FolderGit2,
} from 'lucide-react';
import { toast } from 'sonner';
import { useConfirm } from '@/components/ui/ConfirmDialog';
import { useNotificationStore } from '@/stores/notificationStore';
import { IndexingPipelineStepper } from '@/components/repository/IndexingPipelineStepper';
import { SmartRepoInput } from '@/components/repository/SmartRepoInput';

export const ProjectDetailPage: React.FC = () => {
  const { projectId } = useParams<{ projectId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { confirm } = useConfirm();
  const { addNotification } = useNotificationStore();

  const [isImportOpen, setIsImportOpen] = useState(false);
  const [isRenameOpen, setIsRenameOpen] = useState(false);
  const [repoUrl, setRepoUrl] = useState('');
  const [gitRef, setGitRef] = useState('');
  const [newProjectName, setNewProjectName] = useState('');

  // Fetch project details with 2s polling while indexing
  const { data: project, isLoading } = useQuery({
    queryKey: ['project', projectId],
    queryFn: () => projectService.get(projectId!),
    enabled: !!projectId,
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data) return false;
      const hasBuilding = data.repositories.some(
        (r) => r.status !== 'ready' && r.status !== 'failed'
      );
      return hasBuilding ? 2000 : false;
    },
  });

  const activeRepo = project?.repositories.find((r) => r.isActive);
  const getRepoName = (url?: string) => (url ? url.replace('https://github.com/', '') : 'Repository');

  // Active polling on indexing repositories via GET /api/repositories/{id}
  // (This triggers backend RefreshAsync to update status from RAG service)
  React.useEffect(() => {
    if (!project?.repositories) return;

    const indexingRepos = project.repositories.filter(
      (r) => r.status !== 'ready' && r.status !== 'failed'
    );
    if (indexingRepos.length === 0) return;

    let isMounted = true;
    const interval = setInterval(async () => {
      try {
        await Promise.all(
          indexingRepos.map(async (r) => {
            const status = await repositoryService.getStatus(r.id);
            if (!isMounted) return;
            if (status.status !== r.status) {
              queryClient.invalidateQueries({ queryKey: ['project', projectId] });
              if (status.status === 'ready') {
                addNotification({
                  sender: 'AI Indexer',
                  text: `Repository ${getRepoName(r.repoUrl)} đã index xong! Sẵn sàng cho AI Chat.`,
                  gradient: 'linear-gradient(140deg,#10B981,#059669)',
                  emoji: '⚡',
                  isRag: true,
                  href: `/projects/${projectId}`,
                });
              } else if (status.status === 'failed') {
                toast.error(`Repository ${getRepoName(r.repoUrl)} index thất bại: ${status.errorCode || 'Lỗi không xác định'}`);
              }
            }
          })
        );
      } catch {
        // Stale or transient error, retry on next interval
      }
    }, 2000);

    return () => {
      isMounted = false;
      clearInterval(interval);
    };
  }, [project?.repositories, projectId, queryClient]);

  // Rename project mutation
  const renameMutation = useMutation({
    mutationFn: (name: string) => projectService.update(projectId!, { name }),
    onSuccess: (updated) => {
      queryClient.setQueryData(['project', projectId], updated);
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      setIsRenameOpen(false);
      toast.success('Đã cập nhật tên dự án.');
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Import repository mutation
  const importMutation = useMutation({
    mutationFn: (data: { repoUrl: string; ref?: string }) =>
      repositoryService.import(projectId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['project', projectId] });
      setIsImportOpen(false);
      setRepoUrl('');
      setGitRef('');
      addNotification({
        sender: 'AI Indexer',
        text: `Đã thêm repository ${getRepoName(repoUrl)}. Đang tiến hành lập chỉ mục mã nguồn...`,
        gradient: 'linear-gradient(140deg,#10B981,#059669)',
        emoji: '⚡',
        isRag: true,
        href: `/projects/${projectId}`,
      });
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Delete repository mutation
  const deleteRepoMutation = useMutation({
    mutationFn: (repoId: string) => repositoryService.delete(repoId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['project', projectId] });
      toast.success('Đã xóa repository khỏi dự án.');
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  const handleImportSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!repoUrl.trim()) return;

    const isValidGithub = /^https:\/\/github\.com\/[\w.-]+\/[\w.-]+/.test(repoUrl.trim());
    if (!isValidGithub) {
      toast.error('URL không hợp lệ. Vui lòng nhập URL GitHub công khai (ví dụ: https://github.com/org/repo)');
      return;
    }

    importMutation.mutate({
      repoUrl: repoUrl.trim(),
      ref: gitRef.trim() || undefined,
    });
  };

  if (isLoading) {
    return (
      <>
        <Header breadcrumbs={[{ label: 'Projects', href: '/projects' }, { label: 'Loading...' }]} />
        <main className="min-h-screen pt-14 bg-background text-foreground flex items-center justify-center">
          <div className="flex flex-col items-center gap-2 text-muted-foreground">
            <Loader2 className="w-5 h-5 animate-spin text-foreground" />
            <span className="text-xs">Loading project...</span>
          </div>
        </main>
      </>
    );
  }

  if (!project) {
    return (
      <>
        <Header breadcrumbs={[{ label: 'Projects', href: '/projects' }, { label: 'Not Found' }]} />
        <main className="min-h-screen pt-14 bg-background text-foreground flex flex-col items-center justify-center p-6 text-center">
          <AlertCircle className="w-8 h-8 text-destructive mb-2" />
          <h2 className="text-base font-semibold text-foreground mb-1">Dự án không tồn tại</h2>
          <p className="text-xs text-muted-foreground mb-4">Dự án này có thể đã bị xóa hoặc bạn không có quyền truy cập.</p>
          <button
            onClick={() => navigate('/dashboard')}
            className="px-3 py-1.5 rounded-md bg-foreground text-background text-xs font-medium hover:bg-foreground/90 transition-all shadow-xs"
          >
            Quay lại Dashboard
          </button>
        </main>
      </>
    );
  }

  const readyRepos = project.repositories.filter((r) => r.status === 'ready');
  const isAnyIndexing = project.repositories.some((r) => r.status !== 'ready' && r.status !== 'failed');

  return (
    <>
      <Header
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: project.name },
        ]}
      />

      <main id="main-content" className="min-h-screen pt-14 bg-background text-foreground flex-1">
        <div className="max-w-6xl mx-auto px-6 py-8">
          {/* Project Header */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-6 border-b border-border">
            <div className="flex flex-col gap-1 min-w-0">
              <div className="flex items-center gap-2.5 flex-wrap">
                <h1 className="text-xl font-semibold tracking-tight text-foreground truncate">
                  {project.name}
                </h1>
                <button
                  type="button"
                  onClick={() => {
                    setNewProjectName(project.name);
                    setIsRenameOpen(true);
                  }}
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
                  title="Rename Project"
                  aria-label="Đổi tên dự án"
                >
                  <Edit2 className="w-3.5 h-3.5" />
                </button>
                <span className="text-xs text-muted-foreground">
                  • {project.repositories.length} {project.repositories.length === 1 ? 'repository' : 'repositories'}
                </span>
                {isAnyIndexing && (
                  <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-500 border border-amber-500/20 text-[11px] font-medium">
                    <Loader2 className="w-3 h-3 animate-spin" />
                    <span>Indexing in progress</span>
                  </span>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                Created {formatDate(project.createdAt)} ({formatTimeAgo(project.createdAt)})
              </p>
            </div>

            {/* Action Buttons */}
            <div className="flex items-center gap-2 shrink-0">
              <button
                type="button"
                onClick={() => setIsImportOpen(true)}
                className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md bg-foreground text-background text-xs font-medium hover:bg-foreground/90 transition-all shadow-xs active:scale-[0.98]"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Import Repository</span>
              </button>

              <button
                type="button"
                onClick={() => navigate(`/projects/${projectId}/chat`)}
                disabled={project.repositories.length === 0}
                className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md bg-card text-foreground hover:bg-accent text-xs font-medium border border-border transition-colors disabled:opacity-40"
              >
                <MessageSquareCode className="w-3.5 h-3.5 text-muted-foreground" />
                <span>Chat</span>
              </button>

              <button
                type="button"
                onClick={() => navigate(`/projects/${projectId}/explorer`)}
                disabled={project.repositories.length === 0}
                className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md bg-card text-foreground hover:bg-accent text-xs font-medium border border-border transition-colors disabled:opacity-40"
              >
                <Code2 className="w-3.5 h-3.5 text-muted-foreground" />
                <span>Code Explorer</span>
              </button>
            </div>
          </div>

          {/* Quick Metrics Bar */}
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 my-6">
            <div className="p-3.5 rounded-lg bg-card border border-border">
              <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">Connected Repositories</span>
              <div className="text-xl font-semibold text-foreground mt-1">{project.repositories.length}</div>
            </div>
            <div className="p-3.5 rounded-lg bg-card border border-border">
              <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">Ready for Search</span>
              <div className="text-xl font-semibold text-emerald-500 mt-1">{readyRepos.length}</div>
            </div>
            <div className="col-span-2 sm:col-span-1 p-3.5 rounded-lg bg-card border border-border">
              <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">Active Target</span>
              <div className="text-sm font-medium text-foreground truncate mt-1.5">
                {activeRepo ? getRepoName(activeRepo.repoUrl) : 'Default selection'}
              </div>
            </div>
          </div>

          {/* Repositories List */}
          <section className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-semibold text-foreground">Repositories</h2>
              <span className="text-xs text-muted-foreground">
                {project.repositories.length} total
              </span>
            </div>

            {project.repositories.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border p-12 flex flex-col items-center justify-center text-center">
                <div className="w-9 h-9 rounded-md bg-accent flex items-center justify-center text-muted-foreground mb-3">
                  <FolderGit2 className="w-4 h-4" />
                </div>
                <h3 className="text-sm font-medium text-foreground mb-1">No repositories connected</h3>
                <p className="text-xs text-muted-foreground max-w-sm mb-4">
                  Import a public GitHub repository to start indexing code and querying with AI.
                </p>
                <button
                  type="button"
                  onClick={() => setIsImportOpen(true)}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-foreground text-background text-xs font-medium hover:bg-foreground/90 transition-all shadow-xs"
                >
                  <Plus className="w-3.5 h-3.5" />
                  <span>Import GitHub Repository</span>
                </button>
              </div>
            ) : (
              <div className="flex flex-col gap-3">
                {project.repositories.map((repo) => {
                  const isReady = repo.status === 'ready';
                  const isFailed = repo.status === 'failed';
                  const isProcessing = !isReady && !isFailed;

                  const stages = ['pending', 'cloning', 'parsing', 'embedding', 'ready'];
                  const currentIdx = stages.indexOf(repo.stage || repo.status);

                  return (
                    <article
                      key={repo.id}
                      className="p-4 rounded-lg bg-card border border-border hover:border-border/80 transition-all flex flex-col gap-3.5"
                    >
                      {/* Top Row */}
                      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                        <div className="flex items-center gap-3 min-w-0">
                          <div className="w-8 h-8 rounded-md bg-accent text-foreground flex items-center justify-center shrink-0">
                            <GitFork className="w-4 h-4 text-muted-foreground" />
                          </div>

                          <div className="flex flex-col min-w-0">
                            <div className="flex items-center gap-2 flex-wrap">
                              <span className="text-xs font-semibold text-foreground font-mono truncate">
                                {getRepoName(repo.repoUrl)}
                              </span>
                              {repo.isActive && (
                                <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-primary/10 text-primary border border-primary/20">
                                  Default
                                </span>
                              )}
                              <a
                                href={repo.repoUrl}
                                target="_blank"
                                rel="noreferrer"
                                className="text-muted-foreground hover:text-foreground transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none rounded p-0.5"
                                title="Open in GitHub"
                                aria-label={`Open ${getRepoName(repo.repoUrl)} in GitHub`}
                              >
                                <ExternalLink className="w-3 h-3" />
                              </a>
                            </div>

                            <div className="flex items-center gap-2 text-[11px] text-muted-foreground mt-0.5 font-mono">
                              <span className="flex items-center gap-1 text-foreground/80">
                                <GitBranch className="w-3 h-3 text-muted-foreground" />
                                {repo.ref || 'main'}
                              </span>
                              {repo.commitSha && (
                                <>
                                  <span>•</span>
                                  <span>{repo.commitSha.slice(0, 7)}</span>
                                </>
                              )}
                              <span>•</span>
                              <span>Added {formatDate(repo.createdAt)}</span>
                            </div>
                          </div>
                        </div>

                        {/* Actions */}
                        <div className="flex items-center gap-1.5 shrink-0">
                          <button
                            type="button"
                            onClick={() => navigate(`/projects/${projectId}/chat`)}
                            disabled={!isReady}
                            aria-label={`Chat with repository ${getRepoName(repo.repoUrl)}`}
                            className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md bg-card hover:bg-accent text-foreground text-xs font-medium border border-border transition-colors disabled:opacity-40 focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                          >
                            <MessageSquareCode className="w-3.5 h-3.5 text-muted-foreground" />
                            <span>Chat</span>
                          </button>
                          <button
                            type="button"
                            onClick={() => navigate(`/projects/${projectId}/explorer?repoId=${repo.id}`)}
                            disabled={!isReady}
                            aria-label={`Browse code for ${getRepoName(repo.repoUrl)}`}
                            className="inline-flex items-center gap-1 h-7 px-2.5 rounded-md bg-card hover:bg-accent text-foreground text-xs font-medium border border-border transition-colors disabled:opacity-40 focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                          >
                            <Code2 className="w-3.5 h-3.5 text-muted-foreground" />
                            <span>Browse</span>
                          </button>
                          <button
                            type="button"
                            onClick={async () => {
                              const repoName = getRepoName(repo.repoUrl);
                              const ok = await confirm({
                                title: 'Xóa repository?',
                                description: `Bạn có chắc muốn xóa repository "${repoName}" khỏi dự án này?`,
                                confirmText: 'Xóa repository',
                                variant: 'danger',
                                iconType: 'trash',
                              });
                              if (ok) {
                                deleteRepoMutation.mutate(repo.id);
                              }
                            }}
                            aria-label={`Delete repository ${getRepoName(repo.repoUrl)}`}
                            className="p-1.5 rounded text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors focus-visible:ring-1 focus-visible:ring-destructive focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
                            title="Xóa repository"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </div>

                      {/* Pipeline Stepper */}
                      <IndexingPipelineStepper repo={repo} />

                      {/* Error Banner */}
                      {isFailed && repo.errorCode && (
                        <div className="p-2.5 rounded-md bg-destructive/10 border border-destructive/20 text-destructive text-xs flex items-center gap-2">
                          <AlertCircle className="w-4 h-4 shrink-0" />
                          <span>Indexing failed with code: {repo.errorCode}</span>
                        </div>
                      )}
                    </article>
                  );
                })}
              </div>
            )}
          </section>
        </div>

        {/* Import Modal */}
        {isImportOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 animate-in fade-in duration-150">
            <div className="w-full max-w-md rounded-lg bg-card border border-border shadow-2xl p-5 flex flex-col gap-4 animate-in zoom-in-95 duration-150">
              <div className="flex items-center justify-between pb-3 border-b border-border">
                <div className="flex items-center gap-2">
                  <GitFork className="w-4 h-4 text-primary" />
                  <h3 className="text-sm font-semibold text-foreground">Import GitHub Repository</h3>
                </div>
                <button
                  type="button"
                  onClick={() => setIsImportOpen(false)}
                  aria-label="Close dialog"
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              </div>

              <form onSubmit={handleImportSubmit} className="flex flex-col gap-3.5">
                <SmartRepoInput
                  value={repoUrl}
                  onChange={setRepoUrl}
                  selectedBranch={gitRef}
                  onSelectBranch={setGitRef}
                />

                <div className="flex flex-col gap-1.5">
                  <label className="text-xs font-medium text-foreground" htmlFor="git-ref-input">
                    Branch / Tag / Ref (Optional)
                  </label>
                  <input
                    id="git-ref-input"
                    type="text"
                    value={gitRef}
                    onChange={(e) => setGitRef(e.target.value)}
                    placeholder="main (defaults to primary branch)"
                    className="w-full px-3 py-1.5 rounded-md bg-muted text-foreground placeholder:text-muted-foreground text-xs font-mono border border-border focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:border-primary transition-colors"
                  />
                </div>

                <div className="flex items-center justify-end gap-2 pt-2 border-t border-border">
                  <button
                    type="button"
                    onClick={() => setIsImportOpen(false)}
                    className="px-3 py-1.5 rounded-md text-foreground hover:bg-accent text-xs transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={importMutation.isPending || !repoUrl.trim()}
                    className="px-3.5 py-1.5 rounded-md bg-primary text-primary-foreground text-xs font-semibold hover:bg-primary/90 transition-all disabled:opacity-50 flex items-center gap-1.5 shadow-xs"
                  >
                    {importMutation.isPending ? (
                      <>
                        <Loader2 className="w-3 h-3 animate-spin" />
                        <span>Starting...</span>
                      </>
                    ) : (
                      <span>Import</span>
                    )}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Rename Modal */}
        {isRenameOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 animate-in fade-in duration-150">
            <div className="w-full max-w-sm rounded-lg bg-card border border-border shadow-2xl p-5 flex flex-col gap-4 animate-in zoom-in-95 duration-150">
              <div className="flex items-center justify-between pb-3 border-b border-border">
                <h3 className="text-sm font-semibold text-foreground">Rename Project</h3>
                <button
                  type="button"
                  onClick={() => setIsRenameOpen(false)}
                  aria-label="Close dialog"
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              </div>

              <input
                type="text"
                required
                autoFocus
                value={newProjectName}
                onChange={(e) => setNewProjectName(e.target.value)}
                className="w-full px-3 py-1.5 rounded-md bg-muted text-foreground text-xs border border-border focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:border-primary transition-colors"
              />

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-border">
                <button
                  type="button"
                  onClick={() => setIsRenameOpen(false)}
                  className="px-3 py-1.5 rounded-md text-foreground hover:bg-accent text-xs transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  disabled={renameMutation.isPending || !newProjectName.trim()}
                  onClick={() => renameMutation.mutate(newProjectName.trim())}
                  className="px-3.5 py-1.5 rounded-md bg-primary text-primary-foreground text-xs font-semibold hover:bg-primary/90 transition-all disabled:opacity-50 shadow-xs"
                >
                  Save
                </button>
              </div>
            </div>
          </div>
        )}
      </main>
    </>
  );
};
