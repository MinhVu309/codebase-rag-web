import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, ArrowRight, Loader2, FolderPlus, X, CheckCircle2, LayoutGrid } from 'lucide-react';
import type { ProjectDetailDto } from '@/types/api';

interface LucianProjectEmptyStateProps {
  onCreateProject: (name: string) => Promise<ProjectDetailDto | void>;
  isCreating?: boolean;
  onDismiss?: () => void;
  isModal?: boolean;
  onSuccessCreated?: (project: ProjectDetailDto) => void;
}

export const LucianProjectEmptyState: React.FC<LucianProjectEmptyStateProps> = ({
  onCreateProject,
  isCreating = false,
  onDismiss,
  isModal = false,
  onSuccessCreated,
}) => {
  const navigate = useNavigate();
  // If modal, show input immediately; if empty state, show trigger button first
  const [isInputOpen, setIsInputOpen] = useState(isModal);
  const [projectName, setProjectName] = useState('');
  const [isDone, setIsDone] = useState(false);
  const [createdProject, setCreatedProject] = useState<ProjectDetailDto | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isInputOpen) {
      inputRef.current?.focus();
    }
  }, [isInputOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = projectName.trim();
    if (!trimmed || isCreating) return;

    try {
      const res = await onCreateProject(trimmed);
      if (res) {
        setCreatedProject(res);
        onSuccessCreated?.(res);
      }
      // Reset input state immediately upon success (no flash of celebration)
      setProjectName('');
      setIsInputOpen(false);
    } catch {
      // Error handled by parent toast
    }
  };

  const handleResetForAnother = () => {
    setIsDone(false);
    setIsInputOpen(true);
    setProjectName('');
    setCreatedProject(null);
  };

  return (
    <div className={`w-full flex justify-center ${isModal ? 'p-2' : 'py-8 px-4'}`}>
      {/* Inline styles scoped to Lucian #440 */}
      <style>{`
        .lucian-440-wrap {
          --ux-surface: var(--card);
          --ux-ink: var(--foreground);
          --ux-ink-2: var(--muted-foreground);
          --ux-line: var(--border);
          --ux-accent: var(--primary);
          --ux-accent-ink: var(--primary-foreground);
          --ux-mint: #10b981;
          --ux-warn: #f59e0b;
          --ux-accent-2: #38bdf8;
          position: relative;
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          text-align: center;
        }

        .u440-il {
          width: 176px;
          height: 132px;
          overflow: visible;
          animation: u440-float 3.2s ease-in-out infinite;
        }

        @keyframes u440-float {
          50% { transform: translateY(-7px); }
        }

        .u440-il .sh {
          fill: currentColor;
          opacity: 0.08;
          transform-box: fill-box;
          transform-origin: center;
          animation: u440-sh 3.2s ease-in-out infinite;
        }

        @keyframes u440-sh {
          50% { transform: scaleX(0.84); opacity: 0.04; }
        }

        .u440-il .bk {
          fill: rgb(var(--primary) / 0.35);
        }

        .u440-il .fr {
          fill: rgb(var(--primary));
        }

        .u440-il .fh {
          fill: #ffffff;
          opacity: 0.2;
        }

        .u440-il .doc rect.p {
          fill: rgb(var(--card));
          stroke: rgb(var(--border));
          stroke-width: 1.4;
        }

        .u440-il .doc .ln {
          fill: rgb(var(--muted-foreground) / 0.5);
        }

        .u440-il .doc .hd {
          fill: var(--ux-mint);
        }

        .u440-il .doc {
          opacity: 0;
          transform: translateY(-110px) rotate(-10deg);
          transform-box: view-box;
          transform-origin: 80px 50px;
        }

        .u440-il .fol {
          transform-box: view-box;
          transform-origin: 80px 112px;
        }

        .u440-il .sp {
          fill: var(--ux-warn);
          transform-box: fill-box;
          transform-origin: center;
          animation: u440-tw 2.4s ease-in-out infinite;
        }

        .u440-il .sp:nth-of-type(2) {
          animation-delay: -0.8s;
          fill: var(--ux-mint);
        }

        .u440-il .sp:nth-of-type(3) {
          animation-delay: -1.6s;
          fill: var(--ux-accent-2);
        }

        @keyframes u440-tw {
          0%, 100% { transform: scale(0.6); opacity: 0.35; }
          50% { transform: scale(1.1); opacity: 1; }
        }

        .lucian-440-wrap.is-done .u440-il .doc {
          animation: u440-drop 0.85s cubic-bezier(0.4, 0, 0.6, 1) forwards;
        }

        @keyframes u440-drop {
          0% { opacity: 0; transform: translateY(-110px) rotate(-10deg); }
          20% { opacity: 1; }
          62% { transform: translateY(6px) rotate(2deg); }
          80% { transform: translateY(-3px) rotate(-1deg); }
          100% { opacity: 1; transform: none; }
        }

        .lucian-440-wrap.is-done .u440-il .fol {
          animation: u440-bump 0.9s cubic-bezier(0.3, 1.4, 0.5, 1) 0.38s;
        }

        @keyframes u440-bump {
          0% { transform: none; }
          25% { transform: scale(1.08, 0.9); }
          55% { transform: scale(0.96, 1.05); }
          100% { transform: none; }
        }

        .u440-burst {
          position: absolute;
          left: 50%;
          top: 50%;
          width: 0;
          height: 0;
        }

        .u440-burst i {
          position: absolute;
          left: -4px;
          top: -4px;
          width: 8px;
          height: 8px;
          border-radius: 50%;
          background: var(--c);
          opacity: 0;
        }

        .lucian-440-wrap.is-done .u440-burst i {
          animation: u440-pop 0.75s cubic-bezier(0.2, 0.8, 0.3, 1) 0.42s forwards;
        }

        @keyframes u440-pop {
          0% { opacity: 1; transform: rotate(var(--a)) translateY(0) scale(1); }
          100% { opacity: 0; transform: rotate(var(--a)) translateY(-65px) scale(0.4); }
        }

        .u440-h {
          display: grid;
          margin: 14px 0 6px;
        }

        .u440-h b {
          grid-area: 1/1;
          font-size: 19px;
          font-weight: 700;
          letter-spacing: -0.02em;
          transition: opacity 0.25s ease, transform 0.45s cubic-bezier(0.3, 1.4, 0.5, 1), filter 0.25s;
        }

        .u440-h b+b {
          opacity: 0;
          transform: translateY(10px);
          filter: blur(4px);
        }

        .lucian-440-wrap.is-done .u440-h b:first-child {
          opacity: 0;
          transform: translateY(-10px);
          filter: blur(4px);
        }

        .lucian-440-wrap.is-done .u440-h b+b {
          opacity: 1;
          transform: none;
          filter: none;
          transition-delay: 0.45s;
        }

        @media (prefers-reduced-motion: reduce) {
          .u440-il, .u440-il *, .u440-burst i, .u440-h b {
            animation-duration: 0.001ms !important;
            animation-iteration-count: 1 !important;
            transition-duration: 0.001ms !important;
          }
        }
      `}</style>

      <div
        className={`lucian-440-wrap ${isDone ? 'is-done' : ''} ${
          isModal ? 'w-full' : 'max-w-md w-full p-8 rounded-xl border border-dashed border-border bg-card/60 backdrop-blur-xs shadow-xs'
        }`}
      >
        {/* Floating Folder Illustration */}
        <div className="relative" aria-hidden="true">
          <svg className="u440-il text-foreground" viewBox="0 0 160 120">
            <ellipse className="sh" cx="80" cy="116" rx="52" ry="5" />
            <path className="sp" d="M138 22l1.8 4.4 4.4 1.8-4.4 1.8-1.8 4.4-1.8-4.4-4.4-1.8 4.4-1.8z" />
            <path className="sp" d="M20 40l1.4 3.4 3.4 1.4-3.4 1.4-1.4 3.4-1.4-3.4-3.4-1.4 3.4-1.4z" />
            <path className="sp" d="M128 70l1 2.5 2.5 1-2.5 1-1 2.5-1-2.5-2.5-1 2.5-1z" />
            <g className="fol">
              <path
                className="bk"
                d="M28 34a8 8 0 0 1 8-8h22a6 6 0 0 1 4.6 2.2l4.6 5.8H124a8 8 0 0 1 8 8v58a8 8 0 0 1-8 8H36a8 8 0 0 1-8-8z"
              />
              <g className="doc">
                <rect className="p" x="50" y="24" width="60" height="58" rx="6" />
                <rect className="hd" x="57" y="31" width="20" height="5" rx="2.5" />
                <rect className="ln" x="57" y="42" width="44" height="3.5" rx="1.75" />
                <rect className="ln" x="57" y="50" width="36" height="3.5" rx="1.75" />
              </g>
              <path
                className="fr"
                d="M22 56a8 8 0 0 1 8-8h100a8 8 0 0 1 8 8.6l-4.4 44a8 8 0 0 1-8 7.4H34.4a8 8 0 0 1-8-7.4z"
              />
              <path
                className="fh"
                d="M30 48h100a8 8 0 0 1 8 8.6l-.3 3H22.1l-.1-3.6A8 8 0 0 1 30 48z"
              />
            </g>
          </svg>

          {/* Confetti Particle Burst */}
          <span className="u440-burst">
            <i style={{ ['--a' as any]: '-60deg', ['--c' as any]: 'var(--ux-warn)' }} />
            <i style={{ ['--a' as any]: '-25deg', ['--c' as any]: 'var(--ux-mint)' }} />
            <i style={{ ['--a' as any]: '10deg', ['--c' as any]: 'var(--ux-accent-2)' }} />
            <i style={{ ['--a' as any]: '45deg', ['--c' as any]: 'var(--ux-warn)' }} />
            <i style={{ ['--a' as any]: '-95deg', ['--c' as any]: 'rgb(var(--primary))' }} />
            <i style={{ ['--a' as any]: '80deg', ['--c' as any]: 'var(--ux-mint)' }} />
          </span>
        </div>

        {/* Dynamic Titles */}
        <div className="u440-h text-foreground">
          <b className="text-foreground" aria-hidden={isDone ? 'true' : 'false'}>
            {isModal ? 'Tạo dự án mới' : 'Chưa có dự án nào'}
          </b>
          <b className="text-emerald-500 dark:text-emerald-400" aria-hidden={isDone ? 'false' : 'true'}>
            Dự án đã sẵn sàng! 🎉
          </b>
        </div>

        {/* Subtitle */}
        <p className="text-xs text-muted-foreground max-w-xs mb-5">
          {isDone
            ? `Dự án "${createdProject?.name || projectName}" đã được tạo. Bạn có thể mở dự án ngay hoặc tiếp tục tạo thêm.`
            : 'Bắt đầu phân tích codebase bằng cách tạo workspace dự án của bạn.'}
        </p>

        {/* Interactive Action Area */}
        {!isDone ? (
          !isInputOpen ? (
            <button
              type="button"
              onClick={() => setIsInputOpen(true)}
              className="inline-flex items-center gap-2 h-10 px-5 rounded-full bg-primary text-primary-foreground text-xs font-semibold shadow-md hover:bg-primary/90 hover:scale-[1.02] active:scale-[0.97] transition-all cursor-pointer focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
            >
              <Plus className="w-4 h-4 stroke-[2.5]" />
              <span>Tạo dự án đầu tiên</span>
            </button>
          ) : (
            <form onSubmit={handleSubmit} className="w-full max-w-sm flex flex-col gap-2.5 animate-in fade-in zoom-in-95 duration-200">
              <div className="relative">
                <input
                  ref={inputRef}
                  type="text"
                  required
                  value={projectName}
                  onChange={(e) => setProjectName(e.target.value)}
                  placeholder="Nhập tên dự án (ví dụ: my-rag-service)..."
                  className="w-full h-9 pl-3 pr-8 rounded-lg bg-background border border-border text-foreground text-xs placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary transition-all"
                />
                {!isModal && (
                  <button
                    type="button"
                    onClick={() => setIsInputOpen(false)}
                    className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-muted-foreground hover:text-foreground transition-colors"
                    title="Hủy"
                  >
                    <X className="w-3.5 h-3.5" />
                  </button>
                )}
              </div>

              <div className="flex items-center justify-center gap-2">
                {onDismiss ? (
                  <button
                    type="button"
                    onClick={onDismiss}
                    className="h-8 px-3 rounded-md text-muted-foreground hover:text-foreground text-xs transition-colors"
                  >
                    Hủy
                  </button>
                ) : (
                  <button
                    type="button"
                    onClick={() => setIsInputOpen(false)}
                    className="h-8 px-3 rounded-md text-muted-foreground hover:text-foreground text-xs transition-colors"
                  >
                    Hủy
                  </button>
                )}
                <button
                  type="submit"
                  disabled={isCreating || !projectName.trim()}
                  className="inline-flex items-center gap-1.5 h-8 px-4 rounded-md bg-primary text-primary-foreground text-xs font-semibold hover:bg-primary/90 disabled:opacity-50 transition-all shadow-xs cursor-pointer"
                >
                  {isCreating ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Đang tạo...</span>
                    </>
                  ) : (
                    <>
                      <FolderPlus className="w-3.5 h-3.5" />
                      <span>Tạo ngay</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          )
        ) : (
          <div className="flex flex-col sm:flex-row items-center gap-2.5 animate-in fade-in zoom-in-95 duration-300">
            {createdProject && (
              <button
                type="button"
                onClick={() => navigate(`/projects/${createdProject.id}`)}
                className="inline-flex items-center gap-2 h-9 px-4 rounded-full bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow-md hover:scale-[1.02] active:scale-[0.98] transition-all cursor-pointer"
              >
                <span>Mở dự án vừa tạo</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            )}
            <button
              type="button"
              onClick={handleResetForAnother}
              className="inline-flex items-center gap-1.5 h-9 px-4 rounded-full bg-secondary hover:bg-secondary/80 text-secondary-foreground text-xs font-medium border border-border transition-all cursor-pointer"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Tạo thêm dự án</span>
            </button>
            {onDismiss && (
              <button
                type="button"
                onClick={onDismiss}
                className="inline-flex items-center gap-1.5 h-9 px-3.5 rounded-full text-muted-foreground hover:text-foreground hover:bg-accent text-xs font-medium transition-all cursor-pointer"
              >
                <LayoutGrid className="w-3.5 h-3.5" />
                <span>Xem danh sách</span>
              </button>
            )}
          </div>
        )}

        {/* Accessible live status for screen readers */}
        <p className="sr-only" role="status">
          {isDone ? 'Dự án đã sẵn sàng!' : isModal ? 'Tạo dự án mới' : 'Chưa có dự án nào'}
        </p>
      </div>
    </div>
  );
};
