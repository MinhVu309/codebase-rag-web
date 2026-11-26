import React, { useEffect } from 'react';
import { NavLink, useNavigate, useParams, useLocation } from 'react-router-dom';
import {
  LayoutDashboard,
  FolderKanban,
  GitFork,
  MessageSquareCode,
  Code2,
  Settings,
  ChevronRight,
  CodeXml,
  X,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useUiStore } from '@/stores/uiStore';

export const Sidebar: React.FC = () => {
  const { projectId } = useParams<{ projectId?: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { isMobileSidebarOpen, closeMobileSidebar } = useUiStore();

  // Close mobile sidebar on route change
  useEffect(() => {
    closeMobileSidebar();
  }, [location.pathname, closeMobileSidebar]);

  // Close mobile sidebar on Escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeMobileSidebar();
    };
    if (isMobileSidebarOpen) {
      window.addEventListener('keydown', handleKeyDown);
    }
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isMobileSidebarOpen, closeMobileSidebar]);

  const navItems = [
    { name: 'Dashboard', path: '/dashboard', icon: LayoutDashboard },
    { name: 'Projects', path: '/projects', icon: FolderKanban },
    ...(projectId
      ? [
          { name: 'Repositories', path: `/projects/${projectId}`, icon: GitFork },
          { name: 'AI Chat', path: `/projects/${projectId}/chat`, icon: MessageSquareCode },
          { name: 'Code Explorer', path: `/projects/${projectId}/explorer`, icon: Code2 },
        ]
      : []),
    { name: 'Settings', path: '/settings', icon: Settings },
  ];

  const sidebarContent = (
    <div className="flex flex-col justify-between h-full select-none">
      <div className="flex flex-col">
        {/* Brand Header */}
        <div className="h-14 px-4 border-b border-border flex items-center justify-between">
          <div
            onClick={() => {
              navigate('/dashboard');
              closeMobileSidebar();
            }}
            className="flex items-center gap-2.5 cursor-pointer group"
          >
            <div className="w-7 h-7 rounded-md bg-foreground text-background flex items-center justify-center font-bold text-xs shadow-xs">
              <CodeXml className="w-4 h-4" />
            </div>
            <span className="font-semibold text-sm text-foreground tracking-tight group-hover:text-foreground/80 transition-colors">
              CodeInsight
            </span>
          </div>

          {/* Close button on mobile */}
          <button
            type="button"
            onClick={closeMobileSidebar}
            className="p-1 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted md:hidden transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
            aria-label="Close navigation menu"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Quick Project Info / Selector */}
        {projectId && (
          <div className="p-2 border-b border-border">
            <button
              onClick={() => {
                navigate('/projects');
                closeMobileSidebar();
              }}
              className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-md hover:bg-accent/60 transition-colors text-left group focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
              type="button"
            >
              <div className="flex flex-col truncate">
                <span className="text-[10px] text-muted-foreground uppercase font-medium tracking-wider">
                  Active Project
                </span>
                <span className="text-xs font-medium text-foreground truncate mt-0.5">
                  Workspace
                </span>
              </div>
              <ChevronRight className="w-3.5 h-3.5 text-muted-foreground group-hover:text-foreground transition-colors shrink-0" />
            </button>
          </div>
        )}

        {/* Navigation Section */}
        <div className="px-4 pt-3 pb-1">
          <span className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
            Platform
          </span>
        </div>

        <nav className="flex flex-col gap-0.5 p-2">
          {navItems.map((item) => {
            const Icon = item.icon;
            return (
              <NavLink
                key={item.path}
                to={item.path}
                onClick={closeMobileSidebar}
                className={({ isActive }) =>
                  cn(
                    'flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-xs font-medium transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none',
                    isActive
                      ? 'bg-accent text-accent-foreground font-semibold shadow-xs'
                      : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground'
                  )
                }
              >
                <Icon className="w-4 h-4 shrink-0" />
                <span className="truncate">{item.name}</span>
              </NavLink>
            );
          })}
        </nav>
      </div>

      {/* Footer */}
      <div className="p-3 border-t border-border flex items-center justify-between text-[11px] text-muted-foreground">
        <span>Codebase RAG</span>
        <span className="font-mono text-[10px]">v1.0</span>
      </div>
    </div>
  );

  return (
    <>
      {/* Desktop Persistent Sidebar */}
      <aside className="fixed left-0 top-0 h-screen w-64 bg-card border-r border-border z-40 hidden md:flex flex-col">
        {sidebarContent}
      </aside>

      {/* Mobile Backdrop & Drawer */}
      {isMobileSidebarOpen && (
        <div
          className="fixed inset-0 z-50 bg-black/75 md:hidden animate-in fade-in duration-150"
          onClick={closeMobileSidebar}
          aria-hidden="true"
        >
          <div
            className="fixed inset-y-0 left-0 w-64 bg-card border-r border-border shadow-2xl z-50 flex flex-col animate-in slide-in-from-left duration-200"
            onClick={(e) => e.stopPropagation()}
          >
            {sidebarContent}
          </div>
        </div>
      )}
    </>
  );
};
