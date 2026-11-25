import React, { useState, useEffect } from 'react';
import { Sparkles, Bot, Clock, Search, BookOpen, Cpu } from 'lucide-react';

export const AiThinkingIndicator: React.FC = () => {
  const [seconds, setSeconds] = useState(0);

  useEffect(() => {
    const timer = setInterval(() => {
      setSeconds((prev) => prev + 1);
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  const getStageInfo = (sec: number) => {
    if (sec < 3) {
      return {
        text: 'Phân tích câu hỏi & đối soát cú pháp truy vấn...',
        icon: <Search className="w-3.5 h-3.5 text-primary animate-pulse" />,
        step: 1,
      };
    }
    if (sec < 8) {
      return {
        text: 'Tìm kiếm ngữ nghĩa & đối chiếu vector trên codebase...',
        icon: <Cpu className="w-3.5 h-3.5 text-indigo-400 animate-spin" />,
        step: 2,
      };
    }
    if (sec < 18) {
      return {
        text: 'Trích xuất ngữ cảnh và các đoạn file mã nguồn...',
        icon: <BookOpen className="w-3.5 h-3.5 text-emerald-400 animate-pulse" />,
        step: 3,
      };
    }
    return {
      text: 'Mô hình AI đang tổng hợp câu trả lời kèm trích dẫn...',
      icon: <Sparkles className="w-3.5 h-3.5 text-amber-400 animate-pulse" />,
      step: 4,
    };
  };

  const currentStage = getStageInfo(seconds);

  return (
    <article className="flex items-start gap-3 self-start max-w-2xl w-full animate-in fade-in duration-200">
      <div className="w-7 h-7 rounded-md bg-foreground text-background flex items-center justify-center shrink-0 shadow-xs relative">
        <Bot className="w-3.5 h-3.5" />
        <span className="absolute -top-1 -right-1 w-2.5 h-2.5 rounded-full bg-primary animate-ping" />
      </div>

      <div className="flex-1 flex flex-col gap-2 min-w-0">
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <span className="font-medium text-foreground">CodeInsight AI</span>
          <span>•</span>
          <span className="font-mono text-[10px] text-primary flex items-center gap-1">
            <Clock className="w-2.5 h-2.5" />
            <span>{seconds}s elapsed</span>
          </span>
        </div>

        <div className="p-4 rounded-lg rounded-tl-none bg-card text-foreground border border-border/80 shadow-sm flex flex-col gap-3 relative overflow-hidden">
          {/* Subtle top shimmer bar */}
          <div className="absolute top-0 left-0 right-0 h-0.5 bg-gradient-to-r from-transparent via-primary to-transparent animate-pulse" />

          <div className="flex items-center gap-2.5 text-xs text-foreground font-medium">
            <span className="p-1 rounded-md bg-muted border border-border shrink-0">
              {currentStage.icon}
            </span>
            <span className="truncate">{currentStage.text}</span>
          </div>

          {/* Stepper dots */}
          <div className="flex items-center gap-2 pt-1 border-t border-border/50 text-[10px] font-mono text-muted-foreground">
            <div className="flex items-center gap-1.5">
              {[1, 2, 3, 4].map((s) => (
                <span
                  key={s}
                  className={`w-1.5 h-1.5 rounded-full transition-all duration-300 ${
                    s === currentStage.step
                      ? 'bg-primary scale-125'
                      : s < currentStage.step
                      ? 'bg-emerald-500'
                      : 'bg-muted-foreground/30'
                  }`}
                />
              ))}
            </div>
            <span>Deep retrieval with verifiable source code citations</span>
          </div>
        </div>
      </div>
    </article>
  );
};
