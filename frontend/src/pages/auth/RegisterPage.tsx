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
  CheckCircle2,
  Circle,
} from 'lucide-react';

export const RegisterPage: React.FC = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const { register } = useAuthStore();
  const navigate = useNavigate();

  // Dynamic password rules calculation
  const hasLength = password.length >= 8;
  const hasUpper = /[A-Z]/.test(password);
  const hasNum = /[0-9]/.test(password);
  const hasSym = /[^A-Za-z0-9]/.test(password);

  const isMatched = confirmPassword.length > 0 && password === confirmPassword;
  const isMismatch = confirmPassword.length > 0 && password !== confirmPassword;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !password.trim()) {
      setError('Vui lòng điền đầy đủ Email và Mật khẩu.');
      return;
    }

    if (password.length < 6) {
      setError('Mật khẩu phải chứa ít nhất 6 ký tự.');
      return;
    }

    if (password !== confirmPassword) {
      setError('Mật khẩu xác nhận không khớp.');
      return;
    }

    try {
      setLoading(true);
      setError(null);
      await register({ email: email.trim(), password });
      navigate('/dashboard');
    } catch (err) {
      const errInfo = getErrorMessage(err, 'Đăng ký tài khoản không thành công.');
      setError(errInfo.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <main id="main-content" className="w-full min-h-screen flex items-center justify-center bg-background px-4 py-12 text-on-surface">
      <div className="w-full max-w-[400px] flex flex-col items-center">
        {/* Brand Logo & Title */}
        <div className="flex flex-col items-center text-center mb-8">
          <div className="w-10 h-10 rounded-xl bg-surface-container-high border border-outline-variant flex items-center justify-center text-primary mb-4 shadow-sm">
            <Code2 className="w-5 h-5" />
          </div>
          <h1 className="text-xl font-semibold text-on-surface tracking-tight">
            Create an account
          </h1>
          <p className="text-xs text-on-surface-variant mt-1.5">
            Start indexing repositories and querying code with AI
          </p>
        </div>

        {/* Main Card */}
        <div className="w-full bg-surface rounded-xl border border-outline-variant p-6 shadow-sm">
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
              <label className="text-xs font-medium text-on-surface" htmlFor="password">
                Password
              </label>
              <div className="relative flex items-center">
                <input
                  id="password"
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  required
                  autoComplete="new-password"
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

              {/* Password Checklist (Clean 2-column) */}
              {password.length > 0 && (
                <div className="grid grid-cols-2 gap-1.5 pt-1.5 text-[11px] text-outline">
                  <div className={`flex items-center gap-1.5 ${hasLength ? 'text-secondary font-medium' : ''}`}>
                    {hasLength ? <CheckCircle2 className="w-3 h-3" /> : <Circle className="w-3 h-3" />}
                    <span>8+ characters</span>
                  </div>
                  <div className={`flex items-center gap-1.5 ${hasUpper ? 'text-secondary font-medium' : ''}`}>
                    {hasUpper ? <CheckCircle2 className="w-3 h-3" /> : <Circle className="w-3 h-3" />}
                    <span>1+ uppercase</span>
                  </div>
                  <div className={`flex items-center gap-1.5 ${hasNum ? 'text-secondary font-medium' : ''}`}>
                    {hasNum ? <CheckCircle2 className="w-3 h-3" /> : <Circle className="w-3 h-3" />}
                    <span>1+ number</span>
                  </div>
                  <div className={`flex items-center gap-1.5 ${hasSym ? 'text-secondary font-medium' : ''}`}>
                    {hasSym ? <CheckCircle2 className="w-3 h-3" /> : <Circle className="w-3 h-3" />}
                    <span>1+ symbol</span>
                  </div>
                </div>
              )}
            </div>

            {/* Confirm Password Field */}
            <div className="flex flex-col gap-1.5">
              <div className="flex items-center justify-between text-xs">
                <label className="text-on-surface font-medium" htmlFor="confirmPassword">
                  Confirm Password
                </label>
                {isMatched && <span className="text-[11px] text-secondary">Match</span>}
                {isMismatch && <span className="text-[11px] text-error">Passwords must match</span>}
              </div>
              <div className="relative flex items-center">
                <input
                  id="confirmPassword"
                  name="confirmPassword"
                  type={showConfirmPassword ? 'text' : 'password'}
                  required
                  autoComplete="new-password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  placeholder="••••••••••••"
                  className="w-full h-9 pl-3 pr-9 rounded-lg bg-surface-container-lowest text-on-surface placeholder:text-outline text-xs border border-outline-variant focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-colors"
                />
                <button
                  type="button"
                  aria-label="Toggle confirm password visibility"
                  onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                  className="absolute right-2.5 p-1 rounded text-outline hover:text-on-surface transition-colors"
                >
                  {showConfirmPassword ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                </button>
              </div>
            </div>

            {/* Terms notice */}
            <p className="text-[11px] text-on-surface-variant leading-relaxed">
              By creating an account, you agree to our{' '}
              <a href="#terms" className="text-on-surface hover:text-primary underline">
                Terms of Service
              </a>{' '}
              and{' '}
              <a href="#privacy" className="text-on-surface hover:text-primary underline">
                Privacy Policy
              </a>
              .
            </p>

            {/* Submit Button */}
            <button
              type="submit"
              disabled={loading}
              className="w-full h-9 mt-1 rounded-lg bg-primary hover:bg-primary-container text-on-primary text-xs font-medium flex items-center justify-center gap-2 transition-all active:scale-[0.99] disabled:opacity-60 shadow-sm"
            >
              {loading ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Creating account...</span>
                </>
              ) : (
                <>
                  <span>Create account</span>
                  <ArrowRight className="w-3.5 h-3.5" />
                </>
              )}
            </button>
          </form>
        </div>

        {/* Switcher */}
        <p className="text-xs text-on-surface-variant mt-6 text-center">
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-on-surface hover:text-primary transition-colors">
            Sign in
          </Link>
        </p>
      </div>
    </main>
  );
};
