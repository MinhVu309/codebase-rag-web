import React from 'react';
import { useAuthStore } from '@/stores/authStore';
import { useThemeStore } from '@/stores/themeStore';
import { useUiStore } from '@/stores/uiStore';
import { Sun, Moon, LogOut, Menu, Search } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { LucianNotificationPopover } from './LucianNotificationPopover';

interface HeaderProps {
  breadcrumbs?: Array<{ label: string; href?: string }>;
  activeRepoBadge?: React.ReactNode;
}

export const Header: React.FC<HeaderProps> = ({ breadcrumbs = [], activeRepoBadge }) => {
  const { user, logout } = useAuthStore();
  const { isDark, toggleTheme } = useThemeStore();
  const { toggleMobileSidebar } = useUiStore();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const userInitial = user?.email ? user.email[0].toUpperCase() : 'U';

  return (
    <header className="fixed top-0 left-0 md:left-64 right-0 h-14 bg-background/90 backdrop-blur-md border-b border-border z-30 flex items-center justify-between px-4 sm:px-6">
      {/* Left: Mobile hamburger menu & Breadcrumbs & Global Search */}
      <div className="flex items-center gap-3 min-w-0 flex-1 mr-4">
        {/* Mobile Sidebar Toggle Button */}
        <button
          type="button"
          onClick={toggleMobileSidebar}
          className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted md:hidden transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
          aria-label="Toggle navigation menu"
        >
          <Menu className="w-4 h-4" />
        </button>

        <nav aria-label="Breadcrumbs" className="flex items-center gap-1.5 text-xs text-muted-foreground shrink-0 overflow-hidden">
          {breadcrumbs.map((b, idx) => (
            <React.Fragment key={idx}>
              {idx > 0 && <span className="text-muted-foreground/40 select-none">/</span>}
              {b.href ? (
                <button
                  onClick={() => navigate(b.href!)}
                  className="hover:text-foreground transition-colors cursor-pointer truncate max-w-[120px] sm:max-w-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none rounded px-0.5"
                >
                  {b.label}
                </button>
              ) : (
                <span className="text-foreground font-medium truncate max-w-[120px] sm:max-w-[200px]">
                  {b.label}
                </span>
              )}
            </React.Fragment>
          ))}
        </nav>

        <div className="h-4 w-[1px] bg-border/60 hidden md:block shrink-0" />

        {/* Quick Header Search like design */}
        <div className="relative hidden md:flex items-center w-64 lg:w-72">
          <Search className="w-3.5 h-3.5 absolute left-2.5 text-muted-foreground pointer-events-none" />
          <input
            type="search"
            placeholder="Search codebase, symbols, commits..."
            className="w-full pl-8 pr-3 py-1 rounded-md bg-muted/60 border border-border text-foreground placeholder:text-muted-foreground text-xs focus:outline-none focus:ring-1 focus:ring-primary transition-all"
          />
        </div>

        {activeRepoBadge && (
          <>
            <div className="h-3.5 w-[1px] bg-border hidden lg:block shrink-0" />
            <div className="hidden lg:flex items-center shrink-0">{activeRepoBadge}</div>
          </>
        )}
      </div>

      {/* Right: Theme Toggle, Lucian Notifications Popover & User Info */}
      <div className="flex items-center gap-2 sm:gap-2.5 shrink-0">
        {/* Lucian #074 Notification Popover */}
        <LucianNotificationPopover />

        {/* Theme Toggle */}
        <button
          type="button"
          onClick={toggleTheme}
          className="rounded-lg text-foreground bg-card hover:bg-accent border border-border transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none h-[34px] w-[34px] flex items-center justify-center cursor-pointer shadow-sm"
          title={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
          aria-label={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
        >
          {isDark ? <Moon className="w-4 h-4 text-foreground" /> : <Sun className="w-4 h-4 text-foreground" />}
        </button>

        <div className="h-4 w-[1px] bg-border/60" />

        {/* User Profile */}
        <div className="flex items-center gap-2 pl-0.5">
          <div className="w-7 h-7 rounded-full bg-primary/20 text-primary border border-primary/30 flex items-center justify-center font-semibold text-xs shadow-xs select-none">
            {userInitial}
          </div>
          <span className="hidden sm:inline text-xs font-medium text-foreground truncate max-w-[130px]">
            {user?.email || 'User'}
          </span>
          <button
            type="button"
            onClick={handleLogout}
            className="p-1.5 rounded-md text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[32px] min-w-[32px] flex items-center justify-center"
            title="Đăng xuất"
            aria-label="Đăng xuất"
          >
            <LogOut className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </header>
  );
};
