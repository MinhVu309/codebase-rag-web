import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
import type { MessageDto, CitationDto } from '@/types/api';
import {
  Bot,
  User,
  Check,
  Copy,
  AlertCircle,
  FileCode,
  Clock,
  ExternalLink,
  ChevronDown,
  ChevronRight,
  ThumbsUp,
  ThumbsDown,
} from 'lucide-react';
import { toast } from 'sonner';
import { CopyButton } from '@/components/ui/CopyButton';

interface MessageItemProps {
  message: MessageDto;
  onCitationClick: (citation: CitationDto) => void;
  userEmail?: string;
  projectId?: string;
}

export const MessageItem: React.FC<MessageItemProps> = ({
  message,
  onCitationClick,
  userEmail,
  projectId,
}) => {
  const [copied, setCopied] = useState(false);
  const [feedback, setFeedback] = useState<'up' | 'down' | null>(null);
  const navigate = useNavigate();

  const handleCopy = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    toast.success('Đã sao chép vào bộ nhớ tạm.');
    setTimeout(() => setCopied(false), 2000);
  };

  if (message.role === 'user') {
    return (
      <article className="flex items-start gap-3 self-end max-w-2xl">
        <div className="flex flex-col items-end">
          <div className="flex items-center gap-2 mb-1">
            <span className="text-[10px] font-mono text-muted-foreground">
              {new Date(message.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
            </span>
            <span className="text-xs font-medium text-foreground">
              {userEmail || 'You'}
            </span>
          </div>
          <div className="px-3.5 py-2.5 rounded-lg rounded-tr-none bg-accent text-accent-foreground leading-relaxed text-xs sm:text-sm whitespace-pre-wrap select-text border border-border">
            {message.content}
          </div>
        </div>
        <div className="w-7 h-7 rounded-md bg-muted text-muted-foreground flex items-center justify-center shrink-0 text-xs font-semibold border border-border">
          {userEmail ? userEmail[0].toUpperCase() : <User className="w-3.5 h-3.5" />}
        </div>
      </article>
    );
  }

  // Assistant Message (Linear / Cursor style)
  return (
    <article className="flex items-start gap-3 self-start max-w-3xl w-full">
      {/* Bot Icon */}
      <div className="w-7 h-7 rounded-md bg-foreground text-background flex items-center justify-center shrink-0 shadow-xs">
        <Bot className="w-3.5 h-3.5" />
      </div>

      <div className="flex flex-col flex-1 min-w-0">
        {/* Header */}
        <div className="flex items-center gap-2 mb-1.5 text-xs text-muted-foreground">
          <span className="font-medium text-foreground">CodeInsight</span>
          <span>•</span>
          <span className="font-mono text-[10px]">
            {new Date(message.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
          </span>
          {message.latencyMs !== null && (
            <>
              <span>•</span>
              <span className="font-mono text-[10px]">{message.latencyMs}ms</span>
            </>
          )}
        </div>

        {/* Content Box */}
        <div className="p-4 rounded-lg rounded-tl-none bg-card text-foreground border border-border flex flex-col gap-3">
          {/* Error Notice */}
          {message.errorCode ? (
            <div className="p-3 rounded-md bg-destructive/10 border border-destructive/20 flex items-start gap-2 text-xs text-destructive">
              <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
              <div>
                <strong>Lỗi RAG: {message.errorCode}.</strong> Không thể xử lý yêu cầu. Vui lòng thử lại.
              </div>
            </div>
          ) : (
            <div className="prose prose-sm dark:prose-invert max-w-none text-foreground leading-relaxed select-text text-xs sm:text-sm">
              <ReactMarkdown
                remarkPlugins={[remarkGfm]}
                rehypePlugins={[rehypeSanitize]}
                components={{
                  code({ node, className, children, ...props }) {
                    const match = /language-(\w+)/.exec(className || '');
                    const isInline = !match && !String(children).includes('\n');

                    if (isInline) {
                      return (
                        <code className="font-mono text-xs px-1.5 py-0.5 rounded bg-muted text-foreground font-medium border border-border">
                          {children}
                        </code>
                      );
                    }

                    return (
                      <div className="rounded-md bg-muted/60 overflow-hidden my-3 border border-border">
                        <div className="bg-muted px-3 py-1.5 flex items-center justify-between text-xs font-mono text-muted-foreground border-b border-border">
                          <span className="text-[11px] font-medium text-foreground">{match ? match[1] : 'code'}</span>
                          <CopyButton text={String(children).replace(/\n$/, '')} showLabel size="xs" />
                        </div>
                        <div className="p-3 font-mono text-xs overflow-x-auto text-foreground">
                          <pre className="m-0 p-0 bg-transparent overflow-visible leading-relaxed">{children}</pre>
                        </div>
                      </div>
                    );
                  },
                }}
              >
                {message.content}
              </ReactMarkdown>
            </div>
          )}

          {/* Citations list */}
          {message.citations && message.citations.length > 0 && (
            <div className="flex flex-col gap-2 pt-3 border-t border-border">
              <div className="flex items-center justify-between text-[11px] text-muted-foreground font-mono">
                <span className="font-medium text-foreground/80 uppercase tracking-wider text-[10px]">
                  Citations ({message.citations.length})
                </span>
                {projectId && (
                  <button
                    type="button"
                    onClick={() => navigate(`/projects/${projectId}/explorer`)}
                    className="hover:text-foreground transition-colors flex items-center gap-1 text-[10px]"
                  >
                    <span>Open in Code Explorer</span>
                    <ExternalLink className="w-2.5 h-2.5" />
                  </button>
                )}
              </div>

              <div className="flex flex-wrap gap-1.5">
                {message.citations.map((c) => (
                  <div
                    key={c.refId}
                    className="inline-flex items-center rounded-md bg-muted hover:bg-accent border border-border transition-colors text-xs font-mono overflow-hidden"
                  >
                    <button
                      type="button"
                      onClick={() => onCitationClick(c)}
                      className="flex items-center gap-1.5 px-2 py-1 text-foreground hover:text-primary transition-colors text-left"
                      title="Quick preview in side drawer"
                    >
                      <span className="font-semibold text-primary">[{c.refId}]</span>
                      <span className="truncate max-w-[160px]">
                        {c.filePath.split('/').pop()}
                      </span>
                      <span className="text-muted-foreground text-[10px]">
                        :{c.startLine}-{c.endLine}
                      </span>
                    </button>

                    {projectId && (
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          navigate(
                            `/projects/${projectId}/explorer?file=${encodeURIComponent(
                              c.filePath
                            )}&lineStart=${c.startLine}&lineEnd=${c.endLine}`
                          );
                        }}
                        className="px-1.5 py-1 text-muted-foreground hover:text-foreground hover:bg-accent border-l border-border transition-colors"
                        title="Jump to full code explorer"
                      >
                        <ExternalLink className="w-3 h-3" />
                      </button>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Footer Bar */}
          <div className="pt-2 flex items-center justify-between gap-2 border-t border-border text-xs">
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => handleCopy(message.content)}
                aria-label="Copy assistant message"
                className="px-2 py-1 rounded hover:bg-muted text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1 text-xs focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px]"
              >
                {copied ? <Check className="w-3 h-3 text-emerald-500" /> : <Copy className="w-3 h-3" />}
                <span>{copied ? 'Copied' : 'Copy'}</span>
              </button>

              <button
                type="button"
                onClick={() => {
                  setFeedback(feedback === 'up' ? null : 'up');
                  toast.success('Cảm ơn phản hồi của bạn!');
                }}
                aria-label="Helpful response"
                aria-pressed={feedback === 'up'}
                className={`p-1 rounded transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center ${
                  feedback === 'up'
                    ? 'text-emerald-500 bg-emerald-500/10'
                    : 'text-muted-foreground hover:text-foreground hover:bg-muted'
                }`}
                title="Good response"
              >
                <ThumbsUp className="w-3 h-3" />
              </button>

              <button
                type="button"
                onClick={() => {
                  setFeedback(feedback === 'down' ? null : 'down');
                  toast.info('Ghi nhận phản hồi.');
                }}
                aria-label="Unhelpful response"
                aria-pressed={feedback === 'down'}
                className={`p-1 rounded transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[28px] min-w-[28px] flex items-center justify-center ${
                  feedback === 'down'
                    ? 'text-destructive bg-destructive/10'
                    : 'text-muted-foreground hover:text-foreground hover:bg-muted'
                }`}
                title="Poor response"
              >
                <ThumbsDown className="w-3 h-3" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </article>
  );
};
