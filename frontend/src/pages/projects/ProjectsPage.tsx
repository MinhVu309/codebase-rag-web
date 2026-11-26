import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { projectService } from '@/services/projectService';
import { Header } from '@/components/layout/Header';
import { useNavigate } from 'react-router-dom';
import { formatTimeAgo, formatDate, getErrorMessage } from '@/lib/utils';
import {
  FolderKanban,
  Plus,
  GitFork,
  MessageSquareCode,
  Trash2,
  Search,
  Code2,
  X,
  Loader2,
  Clock,
  ArrowRight,
} from 'lucide-react';
import { toast } from 'sonner';
import { useConfirm } from '@/components/ui/ConfirmDialog';
import { useNotificationStore } from '@/stores/notificationStore';

export const ProjectsPage: React.FC = () => {
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newProjectName, setNewProjectName] = useState('');
  const [searchQuery, setSearchQuery] = useState('');

  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { confirm } = useConfirm();
  const { addNotification } = useNotificationStore();

  const { data: projects = [], isLoading } = useQuery({
    queryKey: ['projects'],
    queryFn: projectService.list,
  });

  const createMutation = useMutation({
    mutationFn: (name: string) => projectService.create({ name }),
    onSuccess: (newProj) => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      setIsCreateOpen(false);
      setNewProjectName('');
      addNotification({
        sender: 'Workspace',
        text: `Dự án "${newProj.name}" đã được tạo thành công!`,
        gradient: 'linear-gradient(140deg,#3D8BFF,#22C3A6)',
        emoji: '📁',
        isRag: false,
        href: `/projects/${newProj.id}`,
      });
      navigate(`/projects/${newProj.id}`);
    },
    onError: (err) => {
      const errInfo = getErrorMessage(err);
      toast.error(errInfo.message);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => projectService.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      toast.success('Đã xóa dự án.');
    },
    onError: (err) => {
      const errInfo = getErrorMessage(err);
      toast.error(errInfo.message);
    },
  });

  const handleCreateSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newProjectName.trim()) return;
    createMutation.mutate(newProjectName.trim());
  };

  const filteredProjects = projects.filter((p) =>
    p.name.toLowerCase().includes(searchQuery.toLowerCase())
  );

  return (
    <>
      <Header breadcrumbs={[{ label: 'Dashboard', href: '/dashboard' }, { label: 'Projects' }]} />

      <main id="main-content" className="min-h-screen pt-14 bg-background text-foreground flex-1">
        <div className="max-w-6xl mx-auto px-6 py-8">
          {/* Header */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-6 border-b border-border">
            <div>
              <h1 className="text-xl font-semibold tracking-tight text-foreground">Projects</h1>
              <p className="text-xs text-muted-foreground mt-1">
                All indexed codebase workspaces ({projects.length})
              </p>
            </div>
            <button
              type="button"
              onClick={() => setIsCreateOpen(true)}
              className="inline-flex items-center gap-2 h-8 px-3.5 rounded-md bg-primary hover:bg-primary/90 text-primary-foreground text-xs font-semibold shadow-xs transition-all active:scale-[0.98]"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Create Project</span>
            </button>
          </div>

          {/* Filter Bar */}
          <div className="relative max-w-sm w-full my-6">
            <Search className="w-3.5 h-3.5 text-muted-foreground absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="text"
              placeholder="Filter projects by name..."
              aria-label="Filter projects by name"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full bg-card border border-border rounded-md pl-8 pr-8 py-1.5 text-xs text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:border-primary transition-all"
            />
            {searchQuery && (
              <button
                type="button"
                onClick={() => setSearchQuery('')}
                aria-label="Clear filter"
                className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground p-1 rounded focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            )}
          </div>

          {/* Projects Grid */}
          {isLoading ? (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {[1, 2, 3].map((i) => (
                <div key={i} className="h-36 rounded-lg bg-card border border-border animate-pulse" />
              ))}
            </div>
          ) : filteredProjects.length === 0 ? (
            <div className="p-12 rounded-lg border border-dashed border-border flex flex-col items-center justify-center text-center gap-2">
              <FolderKanban className="w-6 h-6 text-muted-foreground" />
              <p className="text-xs text-muted-foreground">No projects found.</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {filteredProjects.map((project) => (
                <div
                  key={project.id}
                  onClick={() => navigate(`/projects/${project.id}`)}
                  className="p-4 rounded-lg bg-card border border-border hover:border-border/80 hover:bg-accent/40 transition-all flex flex-col justify-between group cursor-pointer"
                >
                  <div>
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-1.5">
                        <span
                          className={`w-1.5 h-1.5 rounded-full ${
                            project.repositoryCount > 0 ? 'bg-emerald-500' : 'bg-zinc-400 dark:bg-zinc-600'
                          }`}
                        />
                        <span className="text-[11px] text-muted-foreground font-mono">
                          {project.repositoryCount} {project.repositoryCount === 1 ? 'repo' : 'repos'}
                        </span>
                      </div>
                      <button
                        onClick={async (e) => {
                          e.stopPropagation();
                          const ok = await confirm({
                            title: 'Xóa dự án?',
                            description: `Bạn có chắc muốn xóa dự án "${project.name}"? Mọi dữ liệu liên quan sẽ bị xóa vĩnh viễn.`,
                            confirmText: 'Xóa dự án',
                            variant: 'danger',
                            iconType: 'trash',
                          });
                          if (ok) {
                            deleteMutation.mutate(project.id);
                          }
                        }}
                        aria-label={`Xóa dự án ${project.name}`}
                        className="text-muted-foreground hover:text-destructive transition-colors p-1 rounded opacity-0 group-hover:opacity-100 focus-visible:opacity-100 focus-visible:ring-1 focus-visible:ring-destructive focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
                        title="Delete project"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>

                    <h3 className="text-sm font-medium text-foreground group-hover:text-primary transition-colors mt-2 truncate">
                      {project.name}
                    </h3>
                  </div>

                  <div className="flex items-center justify-between gap-2 mt-6 pt-3 border-t border-border/60 text-[11px] text-muted-foreground">
                    <span className="flex items-center gap-1">
                      <Clock className="w-3 h-3" />
                      {formatTimeAgo(project.createdAt)}
                    </span>
                    <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        onClick={() => navigate(`/projects/${project.id}/chat`)}
                        className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
                        title="Chat"
                        aria-label={`Chat với dự án ${project.name}`}
                      >
                        <MessageSquareCode className="w-3.5 h-3.5" />
                      </button>
                      <button
                        type="button"
                        onClick={() => navigate(`/projects/${project.id}/explorer`)}
                        className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center"
                        title="Code Explorer"
                        aria-label={`Code Explorer cho dự án ${project.name}`}
                      >
                        <Code2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Modal */}
        {isCreateOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 animate-in fade-in duration-150">
            <div className="w-full max-w-md rounded-lg bg-card border border-border shadow-2xl p-5 flex flex-col gap-4 animate-in zoom-in-95 duration-150">
              <div className="flex items-center justify-between pb-3 border-b border-border">
                <h3 className="text-sm font-semibold text-foreground">Create New Project</h3>
                <button
                  type="button"
                  onClick={() => setIsCreateOpen(false)}
                  aria-label="Close dialog"
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              </div>

              <form onSubmit={handleCreateSubmit} className="flex flex-col gap-4">
                <div className="flex flex-col gap-1.5">
                  <label className="text-xs font-medium text-foreground">Project Name</label>
                  <input
                    type="text"
                    required
                    autoFocus
                    value={newProjectName}
                    onChange={(e) => setNewProjectName(e.target.value)}
                    placeholder="e.g. backend-services"
                    className="w-full px-3 py-1.5 rounded-md bg-muted text-foreground text-xs border border-border focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:border-primary transition-colors"
                  />
                </div>

                <div className="flex items-center justify-end gap-2 pt-2 border-t border-border">
                  <button
                    type="button"
                    onClick={() => setIsCreateOpen(false)}
                    className="px-3 py-1.5 rounded-md text-foreground hover:bg-accent text-xs transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={createMutation.isPending || !newProjectName.trim()}
                    className="px-3.5 py-1.5 rounded-md bg-primary text-primary-foreground text-xs font-semibold hover:bg-primary/90 transition-all disabled:opacity-50"
                  >
                    {createMutation.isPending ? 'Creating...' : 'Create'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}
      </main>
    </>
  );
};
