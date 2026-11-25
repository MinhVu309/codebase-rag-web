import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  X,
  Bell,
  CheckCircle2,
  FolderPlus,
  Sparkles,
  GitBranch,
  Trash2,
  CheckCheck,
  ExternalLink,
  Clock,
  Layers,
} from 'lucide-react';
import { formatTimeAgo } from '@/lib/utils';

export interface NotificationItem {
  id: string;
  title: string;
  message: string;
  type: 'success' | 'info' | 'warning' | 'error';
  timestamp: string;
  read: boolean;
  href?: string;
}

interface NotificationDrawerProps {
  isOpen: boolean;
  onClose: () => void;
}

const INITIAL_NOTIFICATIONS: NotificationItem[] = [
  {
    id: 'notif-1',
    title: 'Indexing hoàn tất',
    message: 'Repository ponytail (nhánh main) đã được phân tích AST và nhúng vector RAG thành công.',
    type: 'success',
    timestamp: new Date(Date.now() - 1000 * 60 * 12).toISOString(),
    read: false,
    href: '/projects',
  },
  {
    id: 'notif-2',
    title: 'Dự án mới khởi tạo',
    message: 'Dự án testproject2 đã được tạo và kích hoạt trong workspace của bạn.',
    type: 'info',
    timestamp: new Date(Date.now() - 1000 * 60 * 45).toISOString(),
    read: false,
    href: '/projects',
  },
  {
    id: 'notif-3',
    title: 'RAG Service sẵn sàng',
    message: 'Chế độ giả lập RAG (FakeRagClient) đang phục vụ các truy vấn kiểm thử mã nguồn với độ trễ thấp.',
    type: 'info',
    timestamp: new Date(Date.now() - 1000 * 60 * 180).toISOString(),
    read: true,
  },
];

export const NotificationDrawer: React.FC<NotificationDrawerProps> = ({ isOpen, onClose }) => {
  const navigate = useNavigate();
  const [notifications, setNotifications] = useState<NotificationItem[]>(() => {
    try {
      const saved = localStorage.getItem('codeinsight_notifications');
      return saved ? JSON.parse(saved) : INITIAL_NOTIFICATIONS;
    } catch {
      return INITIAL_NOTIFICATIONS;
    }
  });

  useEffect(() => {
    localStorage.setItem('codeinsight_notifications', JSON.stringify(notifications));
  }, [notifications]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const unreadCount = notifications.filter((n) => !n.read).length;

  const handleMarkAllRead = () => {
    setNotifications((prev) => prev.map((n) => ({ ...n, read: true })));
  };

  const handleClearAll = () => {
    setNotifications([]);
  };

  const handleItemClick = (item: NotificationItem) => {
    setNotifications((prev) =>
      prev.map((n) => (n.id === item.id ? { ...n, read: true } : n))
    );
    if (item.href) {
      onClose();
      navigate(item.href);
    }
  };

  const getIcon = (type: NotificationItem['type']) => {
    switch (type) {
      case 'success':
        return <CheckCircle2 className="w-4 h-4 text-emerald-500 shrink-0" />;
      case 'info':
        return <Sparkles className="w-4 h-4 text-primary shrink-0" />;
      case 'warning':
        return <FolderPlus className="w-4 h-4 text-amber-500 shrink-0" />;
      default:
        return <GitBranch className="w-4 h-4 text-primary shrink-0" />;
    }
  };

  return (
    <div className="fixed inset-0 z-50 overflow-hidden">
      {/* Backdrop */}
      <div
        onClick={onClose}
        className="fixed inset-0 bg-black/60 backdrop-blur-xs transition-opacity animate-in fade-in duration-200"
      />

      {/* Drawer Panel */}
      <aside
        className="fixed top-0 bottom-0 right-0 w-full sm:w-[420px] bg-card border-l border-border shadow-2xl flex flex-col z-50 animate-in slide-in-from-right duration-200"
        role="dialog"
        aria-label="Thông báo hệ thống"
      >
        {/* Header */}
        <div className="h-14 px-5 border-b border-border flex items-center justify-between bg-card shrink-0">
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-lg bg-primary/10 text-primary flex items-center justify-center">
              <Bell className="w-4 h-4" />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-foreground flex items-center gap-2">
                <span>Thông báo</span>
                {unreadCount > 0 && (
                  <span className="px-1.5 py-0.5 rounded-full bg-primary/20 text-primary text-[10px] font-mono font-medium">
                    {unreadCount} mới
                  </span>
                )}
              </h2>
              <p className="text-[11px] text-muted-foreground">Cập nhật hoạt động & trạng thái RAG</p>
            </div>
          </div>

          <div className="flex items-center gap-1">
            {unreadCount > 0 && (
              <button
                type="button"
                onClick={handleMarkAllRead}
                className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-accent transition-colors text-xs flex items-center gap-1"
                title="Đánh dấu tất cả đã đọc"
              >
                <CheckCheck className="w-3.5 h-3.5" />
              </button>
            )}
            {notifications.length > 0 && (
              <button
                type="button"
                onClick={handleClearAll}
                className="p-1.5 rounded-md text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors text-xs flex items-center gap-1"
                title="Xóa tất cả thông báo"
              >
                <Trash2 className="w-3.5 h-3.5" />
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-accent transition-colors ml-1"
              aria-label="Đóng ngăn thông báo"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* List Content */}
        <div className="flex-1 overflow-y-auto divide-y divide-border/60">
          {notifications.length === 0 ? (
            <div className="h-full flex flex-col items-center justify-center p-8 text-center text-muted-foreground">
              <div className="w-12 h-12 rounded-full bg-accent/60 flex items-center justify-center mb-3">
                <Bell className="w-5 h-5 text-muted-foreground/60" />
              </div>
              <p className="text-xs font-medium text-foreground mb-1">Không có thông báo mới</p>
              <p className="text-[11px] text-muted-foreground max-w-xs">
                Mọi cập nhật về quá trình index repository, tạo dự án và truy vấn RAG sẽ xuất hiện tại đây.
              </p>
            </div>
          ) : (
            notifications.map((item) => (
              <div
                key={item.id}
                onClick={() => handleItemClick(item)}
                className={`p-4 flex items-start gap-3 transition-colors cursor-pointer group ${
                  item.read ? 'hover:bg-accent/40 opacity-75' : 'bg-primary/5 hover:bg-primary/10'
                }`}
              >
                <div className="mt-0.5">{getIcon(item.type)}</div>

                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between gap-2 mb-1">
                    <h3 className="text-xs font-medium text-foreground truncate group-hover:text-primary transition-colors">
                      {item.title}
                    </h3>
                    <span className="text-[10px] text-muted-foreground shrink-0 flex items-center gap-1 font-mono">
                      <Clock className="w-2.5 h-2.5" />
                      {formatTimeAgo(item.timestamp)}
                    </span>
                  </div>

                  <p className="text-xs text-muted-foreground leading-relaxed">
                    {item.message}
                  </p>

                  {item.href && (
                    <div className="mt-2 flex items-center gap-1 text-[11px] text-primary hover:underline">
                      <span>Xem chi tiết</span>
                      <ExternalLink className="w-3 h-3" />
                    </div>
                  )}
                </div>

                {!item.read && (
                  <span className="w-2 h-2 rounded-full bg-primary shrink-0 mt-1.5" />
                )}
              </div>
            ))
          )}
        </div>

        {/* Footer */}
        <div className="p-3 border-t border-border bg-card/90 flex items-center justify-between text-[11px] text-muted-foreground shrink-0">
          <span className="flex items-center gap-1.5">
            <span className="w-2 h-2 rounded-full bg-emerald-500 inline-block animate-pulse" />
            <span>AST & Vector Indexer: Online</span>
          </span>
          <span className="font-mono text-[10px]">CodeInsight v1.4</span>
        </div>
      </aside>
    </div>
  );
};
