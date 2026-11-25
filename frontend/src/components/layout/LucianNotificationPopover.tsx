import React, { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useNotificationStore, NotificationItem } from '@/stores/notificationStore';

export const LucianNotificationPopover: React.FC = () => {
  const navigate = useNavigate();
  const [isOpen, setIsOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<'all' | 'rag'>('all');
  const [isRinging, setIsRinging] = useState(false);
  const popoverRef = useRef<HTMLDivElement>(null);
  const bellRef = useRef<HTMLButtonElement>(null);

  const {
    notifications,
    activeBanner,
    isBannerRetracting,
    unreadCount,
    removeNotification,
    markAsRead,
    markAllAsRead,
    clearActiveBanner,
  } = useNotificationStore();

  // Play ringing animation whenever a new banner arrives or unread notifications exist on mount
  useEffect(() => {
    if (activeBanner) {
      setIsRinging(true);
    }
  }, [activeBanner]);

  useEffect(() => {
    if (unreadCount > 0) {
      const timer = setTimeout(() => {
        setIsRinging(true);
      }, 1000);
      return () => clearTimeout(timer);
    }
  }, []);

  // Click outside to close
  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (
        isOpen &&
        popoverRef.current &&
        !popoverRef.current.contains(e.target as Node)
      ) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleOutsideClick);
    return () => document.removeEventListener('mousedown', handleOutsideClick);
  }, [isOpen]);

  // Escape key to close
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        setIsOpen(false);
        bellRef.current?.focus();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen]);

  const handleToggleOpen = () => {
    setIsOpen((prev) => {
      const next = !prev;
      if (next && activeBanner) {
        clearActiveBanner();
      }
      return next;
    });
  };

  const handleRowClick = (item: NotificationItem) => {
    markAsRead(item.id);
    if (item.href) {
      setIsOpen(false);
      navigate(item.href);
    }
  };

  const handleMarkAllRead = () => {
    markAllAsRead();
  };

  const filteredNotifications = notifications.filter((n) => {
    if (activeTab === 'rag') return n.isRag;
    return true;
  });

  return (
    <div
      ref={popoverRef}
      className={`u074-container relative ${!isOpen ? 'is-closed' : ''}`}
    >
      <style>{`
        .u074-container {
          position: relative;
          display: inline-block;
        }

        .u074-bell {
          position: relative;
          width: 34px;
          height: 34px;
          border: 1px solid rgb(var(--border));
          padding: 0;
          border-radius: 8px;
          display: grid;
          place-items: center;
          background: rgb(var(--card));
          color: rgb(var(--foreground));
          box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
          transition: transform 0.3s cubic-bezier(0.34, 1.45, 0.64, 1), background-color 0.2s, color 0.2s, border-color 0.2s;
          cursor: pointer;
        }

        .u074-bell:hover {
          background: rgb(var(--accent));
          color: rgb(var(--foreground));
          border-color: rgb(var(--border));
        }

        .u074-bell[aria-expanded="true"] {
          background: rgb(var(--accent));
          border-color: rgb(var(--primary));
          color: rgb(var(--primary));
        }

        .u074-bell:active {
          transform: scale(0.92);
        }

        .u074-bell svg {
          width: 17px;
          height: 17px;
          fill: none;
          stroke: currentColor;
          stroke-width: 2;
          stroke-linecap: round;
          stroke-linejoin: round;
          transform-origin: 50% 15%;
        }

        .u074-bell.ring svg {
          animation: u074-ring 0.9s cubic-bezier(0.2, 0.8, 0.2, 1);
        }

        @keyframes u074-ring {
          10% { transform: rotate(18deg); }
          25% { transform: rotate(-16deg); }
          40% { transform: rotate(12deg); }
          55% { transform: rotate(-8deg); }
          70% { transform: rotate(4deg); }
        }

        /* Red Indicator Dot on Bell */
        .u074-red-dot {
          position: absolute;
          top: 6px;
          right: 6px;
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background: #ef4444;
          box-shadow: 0 0 0 2px rgb(var(--card));
          transition: transform 0.35s cubic-bezier(0.34, 1.45, 0.64, 1), opacity 0.2s ease;
          pointer-events: none;
        }

        .u074-red-dot.unread {
          transform: scale(1);
          opacity: 1;
        }

        .u074-red-dot.read {
          transform: scale(0);
          opacity: 0;
        }

        /* Mini-banner Flyout emerging from Bell */
        .u074-flyout {
          position: absolute;
          right: 0;
          top: calc(100% + 10px);
          width: 320px;
          max-width: calc(100vw - 32px);
          z-index: 65;
          background: rgb(var(--popover));
          border: 1px solid rgb(var(--border));
          border-radius: 12px;
          box-shadow: 0 16px 36px -8px rgba(0, 0, 0, 0.35), 0 0 0 1px rgb(var(--border));
          transform-origin: calc(100% - 17px) top;
          animation: u074-emerge-from-bell 0.38s cubic-bezier(0.34, 1.45, 0.64, 1) forwards;
        }

        .u074-flyout::before {
          content: "";
          position: absolute;
          right: 12px;
          top: -6px;
          width: 11px;
          height: 11px;
          border-radius: 2px;
          background: rgb(var(--popover));
          border-left: 1px solid rgb(var(--border));
          border-top: 1px solid rgb(var(--border));
          transform: rotate(45deg);
        }

        .u074-flyout.retracting {
          animation: u074-retract-to-bell 0.36s cubic-bezier(0.4, 0, 0.2, 1) forwards;
        }

        @keyframes u074-emerge-from-bell {
          0% {
            opacity: 0;
            transform: translateY(-10px) scale(0.65);
          }
          100% {
            opacity: 1;
            transform: translateY(0) scale(1);
          }
        }

        @keyframes u074-retract-to-bell {
          0% {
            opacity: 1;
            transform: translateY(0) scale(1);
          }
          100% {
            opacity: 0;
            transform: translateY(-10px) scale(0.65);
          }
        }

        .u074-flyout-inner {
          display: flex;
          align-items: flex-start;
          gap: 10px;
          padding: 10px 12px;
        }

        .u074-flyout-emoji {
          display: grid;
          place-items: center;
          width: 28px;
          height: 28px;
          border-radius: 8px;
          background: rgb(var(--accent));
          font-size: 14px;
          flex: none;
        }

        .u074-flyout-text {
          flex: 1;
          min-width: 0;
        }

        .u074-flyout-title {
          font-size: 11px;
          font-weight: 700;
          color: rgb(var(--foreground));
          text-transform: uppercase;
          letter-spacing: 0.04em;
          margin-bottom: 2px;
        }

        .u074-flyout-desc {
          font-size: 12px;
          line-height: 1.4;
          color: rgb(var(--muted-foreground));
          margin: 0;
        }

        .u074-flyout-close {
          border: 0;
          background: transparent;
          color: rgb(var(--muted-foreground));
          padding: 4px;
          border-radius: 4px;
          display: grid;
          place-items: center;
          cursor: pointer;
          transition: all 0.15s;
          flex: none;
        }

        .u074-flyout-close:hover {
          background: rgb(var(--accent));
          color: rgb(var(--foreground));
        }

        /* Floating Popover Panel */
        .u074-pan {
          position: absolute;
          right: -8px;
          top: calc(100% + 10px);
          width: 360px;
          max-width: calc(100vw - 24px);
          display: flex;
          flex-direction: column;
          border-radius: 16px;
          background: rgb(var(--popover));
          border: 1px solid rgb(var(--border));
          box-shadow: 0 16px 36px -8px rgba(0, 0, 0, 0.35), 0 0 0 1px rgb(var(--border));
          transform-origin: calc(100% - 24px) -8px;
          transition: transform 0.36s cubic-bezier(0.34, 1.45, 0.64, 1), opacity 0.2s ease;
          z-index: 60;
        }

        .u074-pan::before {
          content: "";
          position: absolute;
          right: 18px;
          top: -6px;
          width: 12px;
          height: 12px;
          border-radius: 2px;
          background: rgb(var(--popover));
          border-left: 1px solid rgb(var(--border));
          border-top: 1px solid rgb(var(--border));
          transform: rotate(45deg);
        }

        .u074-container.is-closed .u074-pan {
          transform: scale(0.65) translateY(-8px);
          opacity: 0;
          pointer-events: none;
          transition: transform 0.22s cubic-bezier(0.2, 0.8, 0.2, 1), opacity 0.16s ease;
        }

        .u074-ph {
          display: flex;
          align-items: center;
          gap: 6px;
          padding: 12px 14px 10px;
          border-bottom: 1px solid rgb(var(--border));
          background: rgb(var(--popover));
          border-top-left-radius: 16px;
          border-top-right-radius: 16px;
        }

        .u074-tab {
          height: 28px;
          padding: 0 12px;
          border: 1px solid transparent;
          border-radius: 999px;
          background: none;
          font-size: 12px;
          font-weight: 600;
          color: rgb(var(--muted-foreground));
          transition: all 0.2s;
          cursor: pointer;
        }

        .u074-tab[aria-selected="true"] {
          background: rgb(var(--foreground));
          color: rgb(var(--background));
          border-color: rgb(var(--foreground));
          font-weight: 700;
        }

        .u074-tab:hover:not([aria-selected="true"]) {
          background: rgb(var(--accent));
          color: rgb(var(--foreground));
        }

        .u074-all {
          margin-left: auto;
          border: 0;
          padding: 4px 6px;
          background: none;
          font-size: 11.5px;
          font-weight: 600;
          color: rgb(var(--primary));
          cursor: pointer;
          transition: opacity 0.2s;
        }

        .u074-all:hover:not(:disabled) {
          text-decoration: underline;
        }

        .u074-all:disabled {
          color: rgb(var(--muted-foreground) / 0.45);
          cursor: default;
          pointer-events: none;
        }

        .u074-list {
          position: relative;
          max-height: 360px;
          margin: 0;
          padding: 8px;
          list-style: none;
          overflow-y: auto;
          overflow-x: hidden;
          background: rgb(var(--popover));
        }

        .u074-item-wrapper {
          position: relative;
          display: flex;
          align-items: center;
          width: 100%;
        }

        .u074-row {
          position: relative;
          display: flex;
          gap: 11px;
          width: 100%;
          padding: 10px 32px 10px 10px;
          border: 1px solid transparent;
          border-radius: 12px;
          background: none;
          text-align: left;
          cursor: pointer;
          transition: background-color 0.15s, border-color 0.15s, transform 0.3s cubic-bezier(0.34, 1.45, 0.64, 1), opacity 0.2s;
        }

        .u074-row:hover {
          background: rgb(var(--accent));
          border-color: rgb(var(--border) / 0.5);
        }

        /* Delete X Button */
        .u074-del-btn {
          position: absolute;
          right: 8px;
          top: 50%;
          transform: translateY(-50%);
          width: 22px;
          height: 22px;
          border: 0;
          border-radius: 6px;
          background: transparent;
          color: rgb(var(--muted-foreground));
          display: grid;
          place-items: center;
          opacity: 0.35;
          transition: all 0.15s ease;
          cursor: pointer;
          z-index: 2;
        }

        .u074-item-wrapper:hover .u074-del-btn {
          opacity: 0.9;
        }

        .u074-del-btn:hover {
          background: rgb(var(--destructive) / 0.15);
          color: rgb(var(--destructive));
          opacity: 1;
          transform: translateY(-50%) scale(1.1);
        }

        .u074-av {
          position: relative;
          width: 36px;
          height: 36px;
          flex: none;
          border-radius: 50%;
          background: var(--g);
          display: flex;
          align-items: center;
          justify-content: center;
          color: #ffffff;
          box-shadow: 0 2px 6px -2px rgba(0, 0, 0, 0.3);
        }

        .u074-av em {
          position: absolute;
          right: -2px;
          bottom: -2px;
          width: 17px;
          height: 17px;
          border-radius: 50%;
          display: grid;
          place-items: center;
          background: rgb(var(--popover));
          border: 1px solid rgb(var(--border));
          font-style: normal;
          font-size: 9px;
        }

        .u074-tx {
          flex: 1;
          min-width: 0;
          font-size: 12.5px;
          line-height: 1.45;
          color: rgb(var(--muted-foreground));
        }

        .u074-tx b {
          color: rgb(var(--foreground));
          font-weight: 700;
          display: block;
          margin-bottom: 2px;
        }

        .u074-tx small {
          display: block;
          margin-top: 4px;
          font-size: 11px;
          font-weight: 500;
          color: rgb(var(--muted-foreground) / 0.85);
        }

        .u074-dot {
          position: absolute;
          right: 32px;
          top: 14px;
          width: 7px;
          height: 7px;
          border-radius: 50%;
          background: rgb(var(--primary));
          box-shadow: 0 0 0 2px rgb(var(--popover));
          transition: transform 0.35s cubic-bezier(0.34, 1.45, 0.64, 1), opacity 0.25s;
        }

        .u074-row.read .u074-dot {
          transform: scale(0);
          opacity: 0;
        }

        .u074-row.read .u074-tx {
          color: rgb(var(--muted-foreground) / 0.8);
        }

        .u074-row.read .u074-tx b {
          color: rgb(var(--foreground) / 0.8);
        }

        .u074-footer {
          padding: 9px 14px;
          border-top: 1px solid rgb(var(--border));
          display: flex;
          align-items: center;
          justify-content: space-between;
          font-size: 11px;
          color: rgb(var(--muted-foreground));
          background: rgb(var(--popover));
          border-bottom-left-radius: 16px;
          border-bottom-right-radius: 16px;
        }

        @media (prefers-reduced-motion: reduce) {
          .u074-bell.ring svg,
          .u074-red-dot,
          .u074-flyout,
          .u074-pan,
          .u074-row,
          .u074-dot {
            animation-duration: 0.001ms !important;
            transition-duration: 0.001ms !important;
          }
        }
      `}</style>

      {/* Bell Trigger Button */}
      <button
        ref={bellRef}
        type="button"
        onClick={handleToggleOpen}
        onAnimationEnd={() => setIsRinging(false)}
        className={`u074-bell ${isRinging ? 'ring' : ''}`}
        aria-expanded={isOpen}
        aria-label={`Thông báo, ${unreadCount} chưa đọc`}
        title="Thông báo"
      >
        <svg viewBox="0 0 22 22" aria-hidden="true">
          <path d="M5.5 15V10a5.5 5.5 0 0 1 11 0v5l1.5 1.5h-14zM9 18.5a2 2 0 0 0 4 0" />
        </svg>

        {/* Red Indicator Dot on Bell: Disappears when all notifications are read */}
        <span
          className={`u074-red-dot ${unreadCount > 0 ? 'unread' : 'read'}`}
          aria-hidden="true"
        />
      </button>

      {/* Mini-banner flyout emerging from bell when new event arrives */}
      {activeBanner && !isOpen && (
        <div
          className={`u074-flyout ${isBannerRetracting ? 'retracting' : ''}`}
          role="status"
          aria-live="polite"
        >
          <div className="u074-flyout-inner">
            <span className="u074-flyout-emoji" aria-hidden="true">
              {activeBanner.emoji}
            </span>
            <div className="u074-flyout-text">
              <div className="u074-flyout-title">{activeBanner.sender}</div>
              <p className="u074-flyout-desc">{activeBanner.text}</p>
            </div>
            <button
              type="button"
              className="u074-flyout-close"
              onClick={clearActiveBanner}
              aria-label="Đóng thông báo này"
              title="Đóng"
            >
              <svg
                viewBox="0 0 16 16"
                width="12"
                height="12"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
              >
                <path d="M4 4l8 8M12 4l-8 8" />
              </svg>
            </button>
          </div>
        </div>
      )}

      {/* Floating Popover Panel */}
      <div
        className="u074-pan"
        role="region"
        aria-label="Danh sách thông báo"
      >
        {/* Header with Tabs & Mark All Read */}
        <div className="u074-ph" role="tablist" aria-label="Phân loại thông báo">
          <button
            type="button"
            className="u074-tab"
            role="tab"
            aria-selected={activeTab === 'all'}
            onClick={() => setActiveTab('all')}
          >
            Tất cả
          </button>
          <button
            type="button"
            className="u074-tab"
            role="tab"
            aria-selected={activeTab === 'rag'}
            onClick={() => setActiveTab('rag')}
          >
            RAG / Codebase
          </button>
          <button
            type="button"
            className="u074-all"
            disabled={unreadCount === 0}
            onClick={handleMarkAllRead}
          >
            Đánh dấu đã đọc
          </button>
        </div>

        {/* Notifications List */}
        <ul className="u074-list" aria-live="polite">
          {filteredNotifications.length === 0 ? (
            <li className="py-10 text-center text-xs text-muted-foreground">
              Không có thông báo nào trong mục này.
            </li>
          ) : (
            filteredNotifications.map((n) => (
              <li key={n.id} className="u074-item-wrapper">
                <button
                  type="button"
                  className={`u074-row ${n.read ? 'read' : ''}`}
                  onClick={() => handleRowClick(n)}
                >
                  <span
                    className="u074-av"
                    style={{ ['--g' as any]: n.gradient }}
                    aria-hidden="true"
                  >
                    <em>{n.emoji}</em>
                  </span>
                  <span className="u074-tx">
                    <b>{n.sender}</b>
                    <span>{n.text}</span>
                    <small>{n.time}</small>
                  </span>
                  <span className="u074-dot" aria-hidden="true" />
                </button>

                {/* Delete button (Dấu x để xóa thông báo cũ) */}
                <button
                  type="button"
                  className="u074-del-btn"
                  onClick={(e) => {
                    e.stopPropagation();
                    removeNotification(n.id);
                  }}
                  aria-label={`Xóa thông báo từ ${n.sender}`}
                  title="Xóa thông báo"
                >
                  <svg
                    viewBox="0 0 16 16"
                    width="12"
                    height="12"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                  >
                    <path d="M4 4l8 8M12 4l-8 8" />
                  </svg>
                </button>
              </li>
            ))
          )}
        </ul>

        {/* Footer */}
        <div className="u074-footer">
          <span className="flex items-center gap-1.5">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 inline-block animate-pulse" />
            <span>AST & RAG Sync: Active</span>
          </span>
          <span>CodeInsight v1.4</span>
        </div>
      </div>
    </div>
  );
};
