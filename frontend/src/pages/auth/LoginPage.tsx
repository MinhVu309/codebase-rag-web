import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '@/stores/authStore';
import { getErrorMessage } from '@/lib/utils';
import {
  Code2,
  Eye,
  EyeOff,
  ArrowRight,
  Loader2,
  AlertCircle,
  Check,
} from 'lucide-react';

export const LoginPage: React.FC = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberDevice, setRememberDevice] = useState(true);
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const { login } = useAuthStore();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !password.trim()) {
      setError('Vui lòng nhập đầy đủ Email và Mật khẩu.');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      await login({ email: email.trim(), password });
      navigate('/dashboard');
    } catch (err) {
      const errInfo = getErrorMessage(err, 'Đăng nhập không thành công. Vui lòng kiểm tra lại thông tin.');
      setError(errInfo.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <main id="main-content" className="w-full min-h-screen flex items-center justify-center bg-background px-4 py-12 text-on-surface">
      <div className="w-full max-w-[380px] flex flex-col items-center">
        {/* Brand Logo & Title */}
        <div className="flex flex-col items-center text-center mb-8">
          <div className="w-10 h-10 rounded-xl bg-surface-container-high border border-outline-variant flex items-center justify-center text-primary mb-4 shadow-sm">
            <Code2 className="w-5 h-5" />
          </div>
          <h1 className="text-xl font-semibold text-on-surface tracking-tight">
            Sign in to CodeInsight
          </h1>
          <p className="text-xs text-on-surface-variant mt-1.5">
            Enter your credentials to access your codebases
          </p>
        </div>

        {/* Main Card */}
        <div className="w-full bg-surface rounded-xl border border-outline-variant p-6 shadow-sm">
          {/* Error Alert */}
          {error && (
            <div className="mb-4 p-3 rounded-lg bg-error-container/15 border border-error/30 flex items-start gap-2.5 text-error text-xs">
              <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
              <div className="flex-1 leading-relaxed">{error}</div>
            </div>
          )}

          <form onSubmit={handleSubmit} className="flex flex-col gap-4">
            {/* Email Field */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-on-surface" htmlFor="email">
                Email
              </label>
              <input
                id="email"
                name="email"
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="name@company.com"
                className="w-full h-9 px-3 rounded-lg bg-surface-container-lowest text-on-surface placeholder:text-outline text-xs border border-outline-variant focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-colors"
              />
            </div>

            {/* Password Field */}
            <div className="flex flex-col gap-1.5">
              <div className="flex items-center justify-between text-xs">
                <label className="text-on-surface font-medium" htmlFor="password">
                  Password
                </label>
                <a href="#reset" className="text-[11px] text-on-surface-variant hover:text-on-surface transition-colors">
                  Forgot?
                </a>
              </div>
              <div className="relative flex items-center">
                <input
                  id="password"
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  required
                  autoComplete="current-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••••••"
                  className="w-full h-9 pl-3 pr-9 rounded-lg bg-surface-container-lowest text-on-surface placeholder:text-outline text-xs border border-outline-variant focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-colors"
                />
                <button
                  type="button"
                  aria-label="Toggle password visibility"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-2.5 p-1 rounded text-outline hover:text-on-surface transition-colors"
                >
                  {showPassword ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                </button>
              </div>
            </div>

            {/* Remember Me Checkbox */}
            <div className="flex items-center justify-between pt-0.5">
              <label className="flex items-center gap-2 cursor-pointer select-none group">
                <input
                  type="checkbox"
                  checked={rememberDevice}
                  onChange={(e) => setRememberDevice(e.target.checked)}
                  className="w-3.5 h-3.5 rounded border border-outline-variant bg-surface-container-lowest text-primary focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none transition-colors cursor-pointer"
                />
                <span className="text-xs text-on-surface-variant group-hover:text-on-surface transition-colors">
                  Remember this device
                </span>
              </label>
            </div>

            {/* Submit Button */}
            <button
              type="submit"
              disabled={loading}
              className="w-full h-9 mt-1 rounded-lg bg-primary hover:bg-primary-container text-on-primary text-xs font-medium flex items-center justify-center gap-2 transition-all active:scale-[0.99] disabled:opacity-60 shadow-sm"
            >
              {loading ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Signing in...</span>
                </>
              ) : (
                <>
                  <span>Sign in</span>
                  <ArrowRight className="w-3.5 h-3.5" />
                </>
              )}
            </button>
          </form>
        </div>

        {/* Switcher */}
        <p className="text-xs text-on-surface-variant mt-6 text-center">
          Don't have an account?{' '}
          <Link to="/register" className="font-medium text-on-surface hover:text-primary transition-colors">
            Sign up
          </Link>
        </p>

        {/* Subtle Brand Footer */}
        <div className="mt-12 text-center text-[11px] text-outline">
          CodeInsight • AI-assisted codebase exploration
        </div>
      </div>
    </main>
  );
};
