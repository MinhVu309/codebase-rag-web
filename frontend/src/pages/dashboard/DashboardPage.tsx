import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { projectService } from '@/services/projectService';
import { useAuthStore } from '@/stores/authStore';
import { useNotificationStore } from '@/stores/notificationStore';
import { Header } from '@/components/layout/Header';
import { useNavigate } from 'react-router-dom';
import { formatTimeAgo, formatDate, getErrorMessage } from '@/lib/utils';
import {
  Plus,
  ArrowRight,
  GitFork,
  MessageSquareCode,
  Code2,
  Trash2,
  Search,
  X,
  MoreVertical,
  LayoutGrid,
  List,
  FolderCode,
  Clock,
  Loader2,
  FolderPlus,
  ChevronDown,
  ArrowUpDown,
  Sparkles,
} from 'lucide-react';
import { toast } from 'sonner';
import { useConfirm } from '@/components/ui/ConfirmDialog';
import { LucianProjectEmptyState } from '@/components/dashboard/LucianProjectEmptyState';

export const DashboardPage: React.FC = () => {
  const { user } = useAuthStore();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { confirm } = useConfirm();

  // Search & Filter state
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'empty'>('all');
  const [sortCriteria, setSortCriteria] = useState<'created' | 'name' | 'repos'>('created');
  const [layoutMode, setLayoutMode] = useState<'grid' | 'list'>('grid');

  // Modal & Menu states
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);

  // Fetch Projects List
  const { data: projects = [], isLoading } = useQuery({
    queryKey: ['projects'],
    queryFn: projectService.list,
  });

  const { addNotification } = useNotificationStore();

  // Create Project Mutation (updates dashboard immediately, never force-navigates)
  const createMutation = useMutation({
    mutationFn: (name: string) => projectService.create({ name }),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      setIsCreateOpen(false);
      // Hiện thông báo từ chiếc chuông thay vì toast góc phải
      addNotification({
        sender: 'Workspace',
        text: `Dự án "${variables}" đã được tạo thành công.`,
        gradient: 'linear-gradient(140deg,#3D8BFF,#22C3A6)',
        emoji: '📁',
        isRag: false,
        href: '/projects',
      });
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Delete Project Mutation
  const deleteMutation = useMutation({
    mutationFn: (id: string) => projectService.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      toast.success('Đã xóa dự án.');
      setActiveMenuId(null);
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Close menus on outside click
  useEffect(() => {
    const handleOutsideClick = () => setActiveMenuId(null);
    window.addEventListener('click', handleOutsideClick);
    return () => window.removeEventListener('click', handleOutsideClick);
  }, []);

  // Keyboard shortcut ⌘K or Ctrl+K to focus search, ⌘N or Ctrl+N to open create modal
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        document.getElementById('project-search-input')?.focus();
      }
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'b') {
        e.preventDefault();
        setIsCreateOpen(true);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Filter & Sort logic
  const filteredProjects = projects
    .filter((p) => {
      const matchSearch = p.name.toLowerCase().includes(searchQuery.toLowerCase());
      if (!matchSearch) return false;
      if (statusFilter === 'active') return p.repositoryCount > 0;
      if (statusFilter === 'empty') return p.repositoryCount === 0;
      return true;
    })
    .sort((a, b) => {
      if (sortCriteria === 'name') return a.name.localeCompare(b.name);
      if (sortCriteria === 'repos') return b.repositoryCount - a.repositoryCount;
      return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
    });

  const totalRepos = projects.reduce((acc, p) => acc + p.repositoryCount, 0);

  return (
    <>
      <Header breadcrumbs={[{ label: 'Dashboard', href: '/dashboard' }, { label: 'Overview' }]} />

      <main id="main-content" className="min-h-screen pt-14 bg-background text-foreground flex-1">
        {/* Top Ambient Glow Line */}
        <div className="w-full h-px bg-gradient-to-r from-transparent via-primary/30 to-transparent" />

        <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6 sm:py-8">
          {/* Header / Welcome Banner */}
          <header className="flex flex-col lg:flex-row lg:items-center justify-between gap-4 pb-6 border-b border-border/70">
            <div className="flex flex-col gap-1.5 min-w-0">
              <div className="flex items-center gap-2.5 flex-wrap">
                <h1 className="text-2xl sm:text-3xl font-semibold tracking-tight text-foreground">
                  Welcome back, {user?.email ? user.email.split('@')[0] : 'Developer'}
                </h1>
                <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-accent text-accent-foreground text-xs font-medium shadow-xs">
                  <span className="w-2 h-2 rounded-full bg-emerald-500 inline-block animate-pulse" />
                  <span>Workspace: Local Shard</span>
                  <span className="text-muted-foreground/60">/</span>
                  <span className="text-foreground font-semibold">{projects.length} Active Projects</span>
                </div>
              </div>
            </div>

            {/* Primary Action Button */}
            <div className="flex items-center gap-2 shrink-0">
              <button
                type="button"
                onClick={() => setIsCreateOpen(true)}
                className="inline-flex items-center gap-2 h-9 px-4 rounded-lg bg-primary hover:bg-primary/90 text-primary-foreground text-xs font-semibold tracking-tight shadow-md hover:shadow-lg transition-all active:scale-[0.98] cursor-pointer"
                id="btn-new-project"
              >
                <Plus className="w-4 h-4 stroke-[2.5]" />
                <span>Create New Project</span>
                <kbd className="hidden sm:inline-block px-1.5 py-0.5 rounded bg-primary-foreground/20 text-primary-foreground font-mono text-[10px] uppercase">

                </kbd>
              </button>
            </div>
          </header>

          {/* Quick Metrics Bar */}
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 my-6">
            <div className="p-3.5 rounded-lg bg-card border border-border shadow-xs">
              <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">Total Projects</span>
              <div className="text-xl font-semibold text-foreground mt-1">{projects.length}</div>
            </div>
            <div className="p-3.5 rounded-lg bg-card border border-border shadow-xs">
              <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">Repositories</span>
              <div className="text-xl font-semibold text-foreground mt-1">{totalRepos}</div>
            </div>
            <div className="col-span-2 sm:col-span-1 p-3.5 rounded-lg bg-card border border-border shadow-xs">
              <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">Indexed Workspaces</span>
              <div className="text-xl font-semibold text-emerald-500 mt-1">
                {projects.filter((p) => p.repositoryCount > 0).length}
              </div>
            </div>
          </div>

          {/* Controls Bar: Search, Filters, Sorters, View Switcher */}
          <section className="p-2 sm:p-2.5 rounded-xl bg-card border border-border shadow-xs flex flex-col md:flex-row items-stretch md:items-center justify-between gap-3 mb-6">
            {/* Search Bar */}
            <div className="relative flex-1 min-w-[240px]">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none" />
              <input
                id="project-search-input"
                type="search"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search projects by name, repository, or tag..."
                aria-label="Search projects by name"
                className="w-full pl-9 pr-8 py-2 rounded-lg bg-background text-foreground placeholder:text-muted-foreground text-xs border border-border/60 focus:outline-none focus:ring-1 focus:ring-primary transition-all"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery('')}
                  aria-label="Clear search query"
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground p-1 rounded focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            {/* Filters & Selectors */}
            <div className="flex items-center gap-2 flex-wrap shrink-0">
              {/* Status Filter */}
              <div className="relative">
                <select
                  id="filter-status"
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value as any)}
                  aria-label="Lọc theo trạng thái dự án"
                  className="appearance-none h-9 pl-3 pr-8 rounded-lg bg-background text-foreground text-xs font-medium cursor-pointer border border-border/60 focus:outline-none focus:ring-1 focus:ring-primary transition-colors"
                >
                  <option value="all">All Projects ({projects.length})</option>
                  <option value="active">Active ({projects.filter((p) => p.repositoryCount > 0).length})</option>
                  <option value="empty">Empty ({projects.filter((p) => p.repositoryCount === 0).length})</option>
                </select>
                <ChevronDown className="w-3.5 h-3.5 absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none" />
              </div>

              {/* Sort Dropdown */}
              <div className="relative">
                <select
                  id="sort-criteria"
                  value={sortCriteria}
                  onChange={(e) => setSortCriteria(e.target.value as any)}
                  aria-label="Sắp xếp danh sách dự án"
                  className="appearance-none h-9 pl-3 pr-8 rounded-lg bg-background text-foreground text-xs font-medium cursor-pointer border border-border/60 focus:outline-none focus:ring-1 focus:ring-primary transition-colors"
                >
                  <option value="created">Sort: Creation Date</option>
                  <option value="name">Sort: Name A-Z</option>
                  <option value="repos">Sort: Most Repositories</option>
                </select>
                <ArrowUpDown className="w-3.5 h-3.5 absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none" />
              </div>

              {/* Layout Toggle (Grid/List) */}
              <div className="flex items-center p-0.5 rounded-lg bg-background border border-border/60 shadow-inner">
                <button
                  type="button"
                  onClick={() => setLayoutMode('grid')}
                  aria-label="Grid layout view"
                  aria-pressed={layoutMode === 'grid'}
                  className={`p-1.5 rounded-md transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center ${layoutMode === 'grid'
                    ? 'bg-accent text-accent-foreground shadow-xs font-semibold'
                    : 'text-muted-foreground hover:text-foreground'
                    }`}
                  title="Grid view"
                >
                  <LayoutGrid className="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => setLayoutMode('list')}
                  aria-label="List layout view"
                  aria-pressed={layoutMode === 'list'}
                  className={`p-1.5 rounded-md transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center ${layoutMode === 'list'
                    ? 'bg-accent text-accent-foreground shadow-xs font-semibold'
                    : 'text-muted-foreground hover:text-foreground'
                    }`}
                  title="List view"
                >
                  <List className="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          </section>

          {/* Recent Projects Header */}
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center gap-2">
              <h2 className="text-base font-semibold tracking-tight text-foreground">Recent Projects</h2>
              <span className="px-2 py-0.5 rounded-full bg-accent text-primary text-xs font-mono font-medium">
                {filteredProjects.length}
              </span>
            </div>
            <span className="text-[11px] font-mono text-muted-foreground uppercase tracking-wider hidden sm:inline-block">
              Real-time AST Sync
            </span>
          </div>

          {/* Loading Skeleton */}
          {isLoading && (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {[1, 2, 3].map((i) => (
                <div key={i} className="h-44 rounded-xl bg-card border border-border animate-pulse" />
              ))}
            </div>
          )}

          {/* Projects View */}
          {!isLoading && filteredProjects.length > 0 && (
            layoutMode === 'grid' ? (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4" id="projects-grid">
                {filteredProjects.map((p) => {
                  const isMenuOpen = activeMenuId === p.id;

                  return (
                    <article
                      key={p.id}
                      onClick={() => navigate(`/projects/${p.id}`)}
                      className="group relative flex flex-col justify-between rounded-xl bg-card hover:bg-accent/30 border border-border hover:border-border/80 transition-all duration-200 shadow-xs hover:shadow-md p-4 cursor-pointer"
                    >
                      <div className="flex flex-col gap-3">
                        {/* Top row: badge & options */}
                        <div className="flex items-start justify-between gap-2">
                          <div className="flex flex-col gap-1 min-w-0">
                            <div className="flex items-center gap-2 flex-wrap">
                              <span
                                className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${p.repositoryCount > 0
                                  ? 'bg-emerald-500/10 text-emerald-500'
                                  : 'bg-muted text-muted-foreground'
                                  }`}
                              >
                                <span
                                  className={`w-1.5 h-1.5 rounded-full ${p.repositoryCount > 0 ? 'bg-emerald-500' : 'bg-muted-foreground'
                                    }`}
                                />
                                {p.repositoryCount > 0 ? 'Active' : 'Empty'}
                              </span>
                              <span className="px-2 py-0.5 rounded-md bg-accent text-muted-foreground text-[11px] font-mono">
                                {p.repositoryCount} {p.repositoryCount === 1 ? 'Repository' : 'Repositories'}
                              </span>
                            </div>
                            <h3 className="text-base font-semibold text-foreground truncate group-hover:text-primary transition-colors mt-1">
                              {p.name}
                            </h3>
                          </div>

                          <div className="relative shrink-0" onClick={(e) => e.stopPropagation()}>
                            <button
                              type="button"
                              onClick={() => setActiveMenuId(isMenuOpen ? null : p.id)}
                              aria-label={`Thao tác dự án ${p.name}`}
                              aria-haspopup="true"
                              aria-expanded={isMenuOpen}
                              className="p-1.5 rounded-lg text-muted-foreground hover:text-foreground hover:bg-accent transition-colors"
                            >
                              <MoreVertical className="w-4 h-4" />
                            </button>

                            {isMenuOpen && (
                              <div className="absolute right-0 top-full mt-1.5 w-44 rounded-lg bg-popover shadow-xl py-1 z-30 border border-border text-xs">
                                <button
                                  type="button"
                                  onClick={() => navigate(`/projects/${p.id}`)}
                                  className="w-full flex items-center gap-2 px-3 py-1.5 text-left text-foreground hover:bg-accent transition-colors"
                                >
                                  <GitFork className="w-3.5 h-3.5 text-muted-foreground" />
                                  <span>Repositories</span>
                                </button>
                                <button
                                  type="button"
                                  onClick={() => navigate(`/projects/${p.id}/chat`)}
                                  className="w-full flex items-center gap-2 px-3 py-1.5 text-left text-foreground hover:bg-accent transition-colors"
                                >
                                  <MessageSquareCode className="w-3.5 h-3.5 text-muted-foreground" />
                                  <span>AI Chat</span>
                                </button>
                                <button
                                  type="button"
                                  onClick={() => navigate(`/projects/${p.id}/explorer`)}
                                  className="w-full flex items-center gap-2 px-3 py-1.5 text-left text-foreground hover:bg-accent transition-colors"
                                >
                                  <Code2 className="w-3.5 h-3.5 text-muted-foreground" />
                                  <span>Code Explorer</span>
                                </button>
                                <div className="h-px bg-border my-1" />
                                <button
                                  type="button"
                                  onClick={async () => {
                                    setActiveMenuId(null);
                                    const ok = await confirm({
                                      title: 'Xóa dự án?',
                                      description: `Bạn có chắc muốn xóa dự án "${p.name}"? Mọi repository, lịch sử chat và dữ liệu liên quan sẽ bị xóa vĩnh viễn.`,
                                      confirmText: 'Xóa dự án',
                                      variant: 'danger',
                                      iconType: 'trash',
                                    });
                                    if (ok) {
                                      deleteMutation.mutate(p.id);
                                    }
                                  }}
                                  className="w-full flex items-center gap-2 px-3 py-1.5 text-left text-destructive hover:bg-destructive/10 transition-colors"
                                >
                                  <Trash2 className="w-3.5 h-3.5" />
                                  <span>Delete Project</span>
                                </button>
                              </div>
                            )}
                          </div>
                        </div>

                        {/* Timestamp metadata */}
                        <div className="flex items-center gap-1.5 text-muted-foreground text-[11px] font-mono">
                          <Clock className="w-3 h-3 text-muted-foreground" />
                          <span>Created {formatDate(p.createdAt)}</span>
                          <span>•</span>
                          <span className="text-foreground/80">{formatTimeAgo(p.createdAt)}</span>
                        </div>

                        {/* Repositories List Preview */}
                        <div className="flex flex-col gap-1 p-2.5 rounded-lg bg-background/60 border border-border/40 text-xs">
                          <div className="flex items-center justify-between text-muted-foreground text-[10px] font-mono uppercase tracking-wider pb-1 border-b border-border/40">
                            <span>Repository Index</span>
                            <span>Vector Sync</span>
                          </div>
                          <div className="flex items-center justify-between py-1 text-xs">
                            <span className="font-mono text-foreground truncate max-w-[180px]">
                              {p.repositoryCount > 0 ? `${p.repositoryCount} repo(s) registered` : 'No repos attached'}
                            </span>
                            <span
                              className={`inline-flex items-center gap-1 text-[11px] font-medium ${p.repositoryCount > 0 ? 'text-emerald-500' : 'text-muted-foreground'
                                }`}
                            >
                              <span
                                className={`w-1.5 h-1.5 rounded-full ${p.repositoryCount > 0 ? 'bg-emerald-500' : 'bg-muted-foreground'
                                  }`}
                              />
                              {p.repositoryCount > 0 ? 'Ready' : 'Pending'}
                            </span>
                          </div>
                        </div>
                      </div>

                      {/* Card Action Footer */}
                      <div className="mt-4 pt-3 flex items-center justify-between border-t border-border/60 text-[11px]">
                        <span className="text-muted-foreground font-mono">
                          RAG Indexed: {p.repositoryCount > 0 ? 'Synchronized' : 'None'}
                        </span>
                        <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
                          <button
                            type="button"
                            onClick={() => navigate(`/projects/${p.id}/chat`)}
                            className="p-1.5 rounded-md hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
                            title="Chat với AI"
                          >
                            <MessageSquareCode className="w-3.5 h-3.5" />
                          </button>
                          <button
                            type="button"
                            onClick={() => navigate(`/projects/${p.id}/explorer`)}
                            className="p-1.5 rounded-md hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
                            title="Code Explorer"
                          >
                            <Code2 className="w-3.5 h-3.5" />
                          </button>
                          <button
                            type="button"
                            onClick={() => navigate(`/projects/${p.id}`)}
                            className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md bg-accent hover:bg-primary hover:text-primary-foreground text-foreground text-xs font-medium transition-colors"
                          >
                            <span>Open Project</span>
                            <ArrowRight className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </div>
                    </article>
                  );
                })}
              </div>
            ) : (
              <div className="flex flex-col divide-y divide-border rounded-xl border border-border bg-card overflow-hidden shadow-xs">
                {filteredProjects.map((p) => (
                  <div
                    key={p.id}
                    onClick={() => navigate(`/projects/${p.id}`)}
                    className="flex items-center justify-between p-4 hover:bg-accent/40 transition-colors cursor-pointer group"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <span
                        className={`w-2 h-2 rounded-full shrink-0 ${p.repositoryCount > 0 ? 'bg-emerald-500' : 'bg-zinc-400 dark:bg-zinc-600'
                          }`}
                      />
                      <div className="flex flex-col truncate">
                        <span className="text-sm font-semibold text-foreground group-hover:text-primary transition-colors truncate">
                          {p.name}
                        </span>
                        <span className="text-xs text-muted-foreground">
                          {p.repositoryCount} repositories • Created {formatDate(p.createdAt)}
                        </span>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 shrink-0">
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          navigate(`/projects/${p.id}/chat`);
                        }}
                        className="p-1.5 rounded-md hover:bg-accent text-muted-foreground hover:text-foreground transition-colors text-xs inline-flex items-center gap-1"
                      >
                        <MessageSquareCode className="w-3.5 h-3.5" />
                        <span className="hidden sm:inline">Chat</span>
                      </button>
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          navigate(`/projects/${p.id}/explorer`);
                        }}
                        className="p-1.5 rounded-md hover:bg-accent text-muted-foreground hover:text-foreground transition-colors text-xs inline-flex items-center gap-1"
                      >
                        <Code2 className="w-3.5 h-3.5" />
                        <span className="hidden sm:inline">Code</span>
                      </button>
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          navigate(`/projects/${p.id}`);
                        }}
                        className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md bg-accent hover:bg-primary hover:text-primary-foreground text-foreground text-xs font-medium transition-colors ml-1"
                      >
                        <span>Open</span>
                        <ArrowRight className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )
          )}

          {/* Empty State */}
          {!isLoading && projects.length === 0 && (
            <LucianProjectEmptyState
              onCreateProject={async (name) => {
                return createMutation.mutateAsync(name);
              }}
              isCreating={createMutation.isPending}
            />
          )}

          {/* Search Empty State */}
          {!isLoading && projects.length > 0 && filteredProjects.length === 0 && (
            <div className="rounded-lg border border-dashed border-border p-12 flex flex-col items-center justify-center text-center">
              <div className="w-9 h-9 rounded-md bg-accent flex items-center justify-center text-muted-foreground mb-3">
                <Search className="w-4 h-4" />
              </div>
              <h3 className="text-sm font-medium text-foreground mb-1">
                Không tìm thấy dự án phù hợp
              </h3>
              <p className="text-xs text-muted-foreground max-w-sm mb-4">
                Không có dự án nào khớp với từ khóa "{searchQuery}".
              </p>
              <button
                type="button"
                onClick={() => setSearchQuery('')}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-secondary hover:bg-secondary/80 text-secondary-foreground text-xs font-medium transition-all"
              >
                <span>Xóa bộ lọc tìm kiếm</span>
              </button>
            </div>
          )}
        </div>

        {/* Create Project Modal with Lucian #440 Experience */}
        {isCreateOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 animate-in fade-in duration-150">
            <div className="relative w-full max-w-md rounded-xl bg-card border border-border shadow-2xl p-6 animate-in zoom-in-95 duration-150">
              <button
                type="button"
                onClick={() => setIsCreateOpen(false)}
                aria-label="Close dialog"
                className="absolute right-4 top-4 p-1 rounded-md text-muted-foreground hover:text-foreground hover:bg-accent transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none z-10"
              >
                <X className="w-4 h-4" />
              </button>

              <LucianProjectEmptyState
                isModal={true}
                onCreateProject={async (name) => {
                  return createMutation.mutateAsync(name);
                }}
                isCreating={createMutation.isPending}
                onDismiss={() => setIsCreateOpen(false)}
              />
            </div>
          </div>
        )}
      </main>
    </>
  );
};
