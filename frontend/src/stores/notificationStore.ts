import { create } from 'zustand';

export interface NotificationItem {
  id: string;
  sender: string;
  text: string;
  time: string;
  gradient: string;
  emoji: string;
  isRag: boolean;
  read: boolean;
  href?: string;
  createdAt: number;
}

export interface NotificationBanner {
  id: string;
  sender: string;
  text: string;
  emoji: string;
}

interface NotificationState {
  notifications: NotificationItem[];
  activeBanner: NotificationBanner | null;
  isBannerRetracting: boolean;
  unreadCount: number;
  addNotification: (
    item: Omit<NotificationItem, 'id' | 'time' | 'read' | 'createdAt'> & { time?: string }
  ) => void;
  removeNotification: (id: string) => void;
  markAsRead: (id: string) => void;
  markAllAsRead: () => void;
  clearActiveBanner: () => void;
}

const STORAGE_KEY = 'codeinsight_lucian_notifs';

const INITIAL_NOTIFICATIONS: NotificationItem[] = [
  {
    id: 'n-1',
    sender: 'AI Indexer',
    text: 'Repository ponytail (main) đã phân tích AST và tạo vector RAG thành công.',
    time: 'Vừa xong',
    gradient: 'linear-gradient(140deg,#10B981,#059669)',
    emoji: '⚡',
    isRag: true,
    read: false,
    href: '/projects',
    createdAt: Date.now() - 60000,
  },
  {
    id: 'n-2',
    sender: 'Workspace',
    text: 'Dự án testproject2 đã khởi tạo xong và sẵn sàng cho việc phân tích codebase.',
    time: '12m ago',
    gradient: 'linear-gradient(140deg,#3D8BFF,#22C3A6)',
    emoji: '📁',
    isRag: false,
    read: false,
    href: '/projects',
    createdAt: Date.now() - 720000,
  },
  {
    id: 'n-3',
    sender: 'Codebase RAG',
    text: 'FakeRagClient đang hoạt động ổn định với độ trễ phản hồi ~120ms.',
    time: '1h ago',
    gradient: 'linear-gradient(140deg,#8B6CFF,#5B8CFF)',
    emoji: '🤖',
    isRag: true,
    read: false,
    createdAt: Date.now() - 3600000,
  },
  {
    id: 'n-4',
    sender: 'System Health',
    text: 'Database PostgreSQL và API backend container đang hoạt động tốt.',
    time: 'Hôm qua',
    gradient: 'linear-gradient(140deg,#FFB547,#FF7A45)',
    emoji: '📊',
    isRag: false,
    read: true,
    createdAt: Date.now() - 86400000,
  },
];

function loadSavedNotifications(): NotificationItem[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return INITIAL_NOTIFICATIONS;
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) && parsed.length > 0 ? parsed : INITIAL_NOTIFICATIONS;
  } catch {
    return INITIAL_NOTIFICATIONS;
  }
}

let bannerTimer: ReturnType<typeof setTimeout> | null = null;
let retractTimer: ReturnType<typeof setTimeout> | null = null;

export const useNotificationStore = create<NotificationState>((set) => {
  const initial = loadSavedNotifications();
  const unread = initial.filter((n) => !n.read).length;

  return {
    notifications: initial,
    activeBanner: null,
    isBannerRetracting: false,
    unreadCount: unread,

    addNotification: (item) => {
      const newNotif: NotificationItem = {
        ...item,
        id: `notif-${Date.now()}-${Math.random().toString(36).substring(2, 6)}`,
        time: item.time || 'Vừa xong',
        read: false,
        createdAt: Date.now(),
      };

      if (bannerTimer) clearTimeout(bannerTimer);
      if (retractTimer) clearTimeout(retractTimer);

      set((state) => {
        const updated = [newNotif, ...state.notifications];
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
        } catch {}

        return {
          notifications: updated,
          unreadCount: updated.filter((n) => !n.read).length,
          activeBanner: {
            id: newNotif.id,
            sender: newNotif.sender,
            text: newNotif.text,
            emoji: newNotif.emoji,
          },
          isBannerRetracting: false,
        };
      });

      // After 3500ms, start smooth retract animation back into bell
      retractTimer = setTimeout(() => {
        set({ isBannerRetracting: true });
        // After 400ms retract animation completes, remove activeBanner
        bannerTimer = setTimeout(() => {
          set({ activeBanner: null, isBannerRetracting: false });
        }, 400);
      }, 3600);
    },

    removeNotification: (id: string) => {
      set((state) => {
        const updated = state.notifications.filter((n) => n.id !== id);
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
        } catch {}

        return {
          notifications: updated,
          unreadCount: updated.filter((n) => !n.read).length,
          activeBanner: state.activeBanner?.id === id ? null : state.activeBanner,
        };
      });
    },

    markAsRead: (id: string) => {
      set((state) => {
        const updated = state.notifications.map((n) =>
          n.id === id ? { ...n, read: true } : n
        );
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
        } catch {}

        return {
          notifications: updated,
          unreadCount: updated.filter((n) => !n.read).length,
        };
      });
    },

    markAllAsRead: () => {
      set((state) => {
        const updated = state.notifications.map((n) => ({ ...n, read: true }));
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
        } catch {}

        return {
          notifications: updated,
          unreadCount: 0,
        };
      });
    },

    clearActiveBanner: () => {
      if (bannerTimer) clearTimeout(bannerTimer);
      if (retractTimer) clearTimeout(retractTimer);
      set({ activeBanner: null, isBannerRetracting: false });
    },
  };
});
