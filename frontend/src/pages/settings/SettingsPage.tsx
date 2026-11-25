import React, { useState } from 'react';
import { useAuthStore } from '@/stores/authStore';
import { useThemeStore, ThemeMode } from '@/stores/themeStore';
import { Header } from '@/components/layout/Header';
import { Button } from '@/components/ui/Button';
import { api } from '@/services/api';
import { formatDate } from '@/lib/utils';
import {
  User,
  Shield,
  Activity,
  LogOut,
  Moon,
  Sun,
  Laptop,
  CheckCircle2,
  RefreshCw,
  Terminal,
  Check,
  Sparkles,
} from 'lucide-react';
import { toast } from 'sonner';
import { useNavigate } from 'react-router-dom';
import { useConfirm } from '@/components/ui/ConfirmDialog';

export const SettingsPage: React.FC = () => {
  const { user, logout } = useAuthStore();
  const { theme, isDark, setTheme, toggleTheme } = useThemeStore();
  const navigate = useNavigate();
  const { confirm } = useConfirm();

  const [healthStatus, setHealthStatus] = useState<string | null>('healthy');
  const [isCheckingHealth, setIsCheckingHealth] = useState(false);

  const checkHealth = async () => {
    try {
      setIsCheckingHealth(true);
      const res = await api.get<{ status: string }>('/health');
      setHealthStatus(res.data.status);
      toast.success('Hệ thống hoạt động bình thường (Healthy).');
    } catch {
      setHealthStatus('unreachable');
      toast.error('Không thể kết nối tới liveness probe /health.');
    } finally {
      setIsCheckingHealth(false);
    }
  };

  const handleLogout = async () => {
    const ok = await confirm({
      title: 'Đăng xuất khỏi CodeInsight?',
      description: 'Bạn có chắc chắn muốn đăng xuất khỏi phiên làm việc hiện tại không?',
      confirmText: 'Đăng xuất',
      cancelText: 'Hủy',
      variant: 'warning',
      iconType: 'logout',
    });
    if (ok) {
      await logout();
      navigate('/login');
    }
  };

  const handleSelectTheme = (mode: ThemeMode) => {
    setTheme(mode);
    toast.success(
      mode === 'dark'
        ? 'Đã chuyển sang Giao diện Tối (Dark Mode).'
        : mode === 'light'
        ? 'Đã chuyển sang Giao diện Sáng (Light Mode).'
        : 'Đã thiết lập theo Hệ thống (System Default).'
    );
  };

  return (
    <>
      <Header breadcrumbs={[{ label: 'Dashboard', href: '/dashboard' }, { label: 'Settings' }]} />

      <main id="main-content" className="pt-20 px-6 sm:px-8 pb-12 flex-1 flex flex-col gap-6 max-w-4xl w-full mx-auto text-on-surface">
        <div>
          <h1 className="text-2xl font-bold text-on-surface tracking-tight font-sans">Cài đặt hệ thống</h1>
          <p className="text-xs text-on-surface-variant mt-1 leading-relaxed">
            Quản lý tài khoản, cấu hình giao diện Sáng / Tối và giám sát trạng thái backend
          </p>
        </div>

        {/* Section 1: User Profile */}
        <div className="p-6 rounded-2xl bg-surface-container-low border border-outline-variant/30 flex flex-col gap-4 shadow-sm">
          <div className="flex items-center gap-3 pb-3 border-b border-outline-variant/20">
            <div className="w-10 h-10 rounded-xl bg-primary-container/20 text-primary flex items-center justify-center font-bold border border-primary/30">
              <User className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-on-surface">Thông tin người dùng</h2>
              <p className="text-xs text-on-surface-variant">Tài khoản xác thực thông qua HttpOnly Cookie</p>
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs font-mono">
            <div className="p-3 rounded-lg bg-surface-container-lowest border border-outline-variant/20 flex flex-col gap-1">
              <span className="text-outline uppercase text-[10px] font-bold">Email</span>
              <span className="text-sm font-sans font-semibold text-on-surface">{user?.email || 'N/A'}</span>
            </div>

            <div className="p-3 rounded-lg bg-surface-container-lowest border border-outline-variant/20 flex flex-col gap-1">
              <span className="text-outline uppercase text-[10px] font-bold">User ID</span>
              <span className="text-xs text-on-surface truncate">{user?.id || 'N/A'}</span>
            </div>

            <div className="p-3 rounded-lg bg-surface-container-lowest border border-outline-variant/20 flex flex-col gap-1">
              <span className="text-outline uppercase text-[10px] font-bold">Ngày tham gia</span>
              <span className="text-xs text-on-surface">{user?.createdAt ? formatDate(user.createdAt) : 'N/A'}</span>
            </div>

            <div className="p-3 rounded-lg bg-surface-container-lowest border border-outline-variant/20 flex flex-col gap-1">
              <span className="text-outline uppercase text-[10px] font-bold">Bảo mật phiên</span>
              <span className="text-xs text-secondary flex items-center gap-1 font-sans font-medium">
                <Shield className="w-3.5 h-3.5" />
                JWT HttpOnly + Refresh Token
              </span>
            </div>
          </div>
        </div>

        {/* Section 2: Appearance & Theme (Fully Interactive Light / Dark Mode) */}
        <div className="p-6 rounded-2xl bg-surface-container-low border border-outline-variant/30 flex flex-col gap-4 shadow-sm">
          <div className="flex items-center justify-between pb-3 border-b border-outline-variant/20">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-xl bg-secondary-container/20 text-secondary flex items-center justify-center font-bold border border-secondary/30">
                {isDark ? <Moon className="w-5 h-5" /> : <Sun className="w-5 h-5 text-tertiary" />}
              </div>
              <div>
                <h2 className="text-base font-bold text-on-surface">Giao diện (Theme)</h2>
                <p className="text-xs text-on-surface-variant">Tùy chỉnh chế độ hiển thị sáng / tối áp dụng toàn hệ thống</p>
              </div>
            </div>

            <span className="font-mono text-xs px-2.5 py-1 rounded-full bg-surface-container-high border border-outline-variant/30 text-on-surface font-semibold">
              {isDark ? '🌙 Dark Active' : '☀️ Light Active'}
            </span>
          </div>

          {/* 3 Interactive Theme Mode Selector Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            {/* Dark Theme Option */}
            <div
              onClick={() => handleSelectTheme('dark')}
              className={`p-4 rounded-xl cursor-pointer border transition-all flex flex-col justify-between gap-3 ${
                theme === 'dark'
                  ? 'bg-surface-container border-primary ring-2 ring-primary/40 shadow-md'
                  : 'bg-surface-container-lowest border-outline-variant/30 hover:border-outline-variant/70'
              }`}
            >
              <div className="flex items-start justify-between">
                <div className="w-8 h-8 rounded-lg bg-[#0d1117] text-[#58a6ff] flex items-center justify-center border border-[#30363d] shadow-sm">
                  <Moon className="w-4 h-4" />
                </div>
                {theme === 'dark' && (
                  <span className="w-5 h-5 rounded-full bg-primary text-on-primary flex items-center justify-center">
                    <Check className="w-3.5 h-3.5 stroke-[3]" />
                  </span>
                )}
              </div>
              <div>
                <div className="text-sm font-bold text-on-surface">Giao diện Tối</div>
                <div className="text-[11px] text-on-surface-variant mt-0.5 leading-relaxed">
                  Tông màu tối GitHub Primer dịu mắt, tránh chữ trắng gắt và chống mỏi mắt khi làm việc ban đêm.
                </div>
              </div>
              {/* Mini Preview Bar */}
              <div className="h-6 rounded bg-[#0d1117] border border-[#30363d] flex items-center px-2 gap-1.5">
                <div className="w-2 h-2 rounded-full bg-[#3fb950]"></div>
                <div className="h-1.5 w-12 rounded bg-[#58a6ff]"></div>
                <div className="h-1.5 w-8 rounded bg-[#30363d]"></div>
              </div>
            </div>

            {/* Light Theme Option */}
            <div
              onClick={() => handleSelectTheme('light')}
              className={`p-4 rounded-xl cursor-pointer border transition-all flex flex-col justify-between gap-3 ${
                theme === 'light'
                  ? 'bg-surface-container border-primary ring-2 ring-primary/40 shadow-md'
                  : 'bg-surface-container-lowest border-outline-variant/30 hover:border-outline-variant/70'
              }`}
            >
              <div className="flex items-start justify-between">
                <div className="w-8 h-8 rounded-lg bg-[#ffffff] text-[#0969da] flex items-center justify-center border border-[#d0d7de] shadow-sm">
                  <Sun className="w-4 h-4 text-[#d29922]" />
                </div>
                {theme === 'light' && (
                  <span className="w-5 h-5 rounded-full bg-primary text-on-primary flex items-center justify-center">
                    <Check className="w-3.5 h-3.5 stroke-[3]" />
                  </span>
                )}
              </div>
              <div>
                <div className="text-sm font-bold text-on-surface">Giao diện Sáng</div>
                <div className="text-[11px] text-on-surface-variant mt-0.5 leading-relaxed">
                  Tông màu sáng chuẩn GitHub, chữ than trầm êm dịu, tương phản chuẩn và không gây chói mắt.
                </div>
              </div>
              {/* Mini Preview Bar */}
              <div className="h-6 rounded bg-[#f6f8fa] border border-[#d0d7de] flex items-center px-2 gap-1.5">
                <div className="w-2 h-2 rounded-full bg-[#1a7f37]"></div>
                <div className="h-1.5 w-12 rounded bg-[#0969da]"></div>
                <div className="h-1.5 w-8 rounded bg-[#d0d7de]"></div>
              </div>
            </div>

            {/* System Default Option */}
            <div
              onClick={() => handleSelectTheme('system')}
              className={`p-4 rounded-xl cursor-pointer border transition-all flex flex-col justify-between gap-3 ${
                theme === 'system'
                  ? 'bg-surface-container border-primary ring-2 ring-primary/40 shadow-md'
                  : 'bg-surface-container-lowest border-outline-variant/30 hover:border-outline-variant/70'
              }`}
            >
              <div className="flex items-start justify-between">
                <div className="w-8 h-8 rounded-lg bg-surface-container-high text-primary flex items-center justify-center border border-outline-variant/40 shadow-sm">
                  <Laptop className="w-4 h-4" />
                </div>
                {theme === 'system' && (
                  <span className="w-5 h-5 rounded-full bg-primary text-on-primary flex items-center justify-center">
                    <Check className="w-3.5 h-3.5 stroke-[3]" />
                  </span>
                )}
              </div>
              <div>
                <div className="text-sm font-bold text-on-surface">Theo Hệ thống</div>
                <div className="text-[11px] text-on-surface-variant mt-0.5 leading-relaxed">
                  Tự động chuyển đổi Sáng / Tối theo cấu hình hệ điều hành Windows/macOS.
                </div>
              </div>
              {/* Mini Preview Bar */}
              <div className="h-6 rounded bg-gradient-to-r from-[#f8f9fe] to-[#0f131c] border border-outline-variant/30 flex items-center px-2 justify-between">
                <span className="text-[9px] font-mono text-outline">AUTO</span>
                <span className="text-[9px] font-mono text-outline">SYNC</span>
              </div>
            </div>
          </div>
        </div>

        {/* Section 3: System Health & Diagnostics */}
        <div className="p-6 rounded-2xl bg-surface-container-low border border-outline-variant/30 flex flex-col gap-4 shadow-sm">
          <div className="flex items-center gap-3 pb-3 border-b border-outline-variant/20">
            <div className="w-10 h-10 rounded-xl bg-tertiary-container/20 text-tertiary flex items-center justify-center font-bold border border-tertiary/30">
              <Activity className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-on-surface">Trạng thái hệ thống (Health Check)</h2>
              <p className="text-xs text-on-surface-variant">Kiểm tra kết nối trực tiếp đến Web Backend liveness probe</p>
            </div>
          </div>

          <div className="flex items-center justify-between p-3.5 rounded-lg bg-surface-container-lowest border border-outline-variant/20">
            <div className="flex items-center gap-3">
              <span className="relative flex h-3 w-3">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-secondary opacity-75" />
                <span className="relative inline-flex rounded-full h-3 w-3 bg-secondary" />
              </span>
              <div>
                <p className="text-sm font-semibold text-on-surface">Backend Liveness Probe</p>
                <p className="text-xs font-mono text-outline">Endpoint: /health • Status: {healthStatus}</p>
              </div>
            </div>

            <Button
              variant="outline"
              size="sm"
              icon={<RefreshCw className={`w-3.5 h-3.5 ${isCheckingHealth ? 'animate-spin' : ''}`} />}
              onClick={checkHealth}
              disabled={isCheckingHealth}
            >
              Ping kiểm tra
            </Button>
          </div>
        </div>

        {/* Section 4: Session & Danger Zone */}
        <div className="p-6 rounded-2xl bg-surface-container-low border border-error/30 flex flex-col gap-4 shadow-sm">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold text-error">Đăng xuất phiên làm việc</h2>
              <p className="text-xs text-on-surface-variant mt-0.5">
                Thu hồi refresh token trên database và xóa sạch cookie xác thực khỏi trình duyệt
              </p>
            </div>
            <Button
              variant="danger"
              size="sm"
              icon={<LogOut className="w-4 h-4" />}
              onClick={handleLogout}
            >
              Đăng xuất
            </Button>
          </div>
        </div>
      </main>
    </>
  );
};
