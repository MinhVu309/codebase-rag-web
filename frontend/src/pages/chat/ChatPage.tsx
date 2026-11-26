import React, { useState, useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { projectService } from '@/services/projectService';
import { chatService } from '@/services/chatService';
import { useAuthStore } from '@/stores/authStore';
import { MessageItem } from '@/components/chat/MessageItem';
import { CitationDrawer } from '@/components/chat/CitationDrawer';
import { Header } from '@/components/layout/Header';
import { getErrorMessage, formatTimeAgo } from '@/lib/utils';
import type { CitationDto, MessageDto } from '@/types/api';
import {
  Send,
  Plus,
  MessageSquare,
  Edit2,
  Trash2,
  SlidersHorizontal,
  Code2,
  AlertCircle,
  Loader2,
  Search,
  BookOpen,
  X,
  Sparkles,
} from 'lucide-react';
import { toast } from 'sonner';
import { useConfirm } from '@/components/ui/ConfirmDialog';
import { useNotificationStore } from '@/stores/notificationStore';
import { repositoryService } from '@/services/repositoryService';
import { IndexingPipelineStepper } from '@/components/repository/IndexingPipelineStepper';
import { AiThinkingIndicator } from '@/components/chat/AiThinkingIndicator';

export const ChatPage: React.FC = () => {
  const { projectId, conversationId } = useParams<{ projectId: string; conversationId?: string }>();
  const { confirm } = useConfirm();
  const { addNotification } = useNotificationStore();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user } = useAuthStore();

  const [question, setQuestion] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [topK, setTopK] = useState(8);
  const [selectedCitation, setSelectedCitation] = useState<CitationDto | null>(null);
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);
  const [isRenameOpen, setIsRenameOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [searchConvQuery, setSearchConvQuery] = useState('');
  const [isTopKOpen, setIsTopKOpen] = useState(false);

  const messagesEndRef = useRef<HTMLDivElement>(null);

  // 1. Fetch Project Details
  const { data: project } = useQuery({
    queryKey: ['project', projectId],
    queryFn: () => projectService.get(projectId!),
    enabled: !!projectId,
  });

  const activeRepo = project?.repositories.find((r) => r.isActive) || project?.repositories[0];
  const isRepoReady = activeRepo?.status === 'ready';
  const getRepoName = (url?: string) => (url ? url.replace('https://github.com/', '') : 'Repository');

  // Active polling on indexing activeRepo via GET /api/repositories/{id}
  useEffect(() => {
    if (!activeRepo || activeRepo.status === 'ready' || activeRepo.status === 'failed') return;

    let isMounted = true;
    const interval = setInterval(async () => {
      try {
        const updated = await repositoryService.getStatus(activeRepo.id);
        if (!isMounted) return;
        if (updated.status !== activeRepo.status) {
          queryClient.invalidateQueries({ queryKey: ['project', projectId] });
          if (updated.status === 'ready') {
            addNotification({
              sender: 'AI Indexer',
              text: 'Repository đã hoàn tất Indexing! Bạn có thể bắt đầu đặt câu hỏi.',
              gradient: 'linear-gradient(140deg,#10B981,#059669)',
              emoji: '⚡',
              isRag: true,
              href: `/projects/${projectId}/chat`,
            });
          } else if (updated.status === 'failed') {
            toast.error(`Repository index thất bại: ${updated.errorCode || 'Lỗi không xác định'}`);
          }
        }
      } catch {
        // ignore transient errors while polling
      }
    }, 2000);

    return () => {
      isMounted = false;
      clearInterval(interval);
    };
  }, [activeRepo?.id, activeRepo?.status, projectId, queryClient]);

  // 2. Fetch Conversations
  const { data: conversations = [], isLoading: isConvsLoading } = useQuery({
    queryKey: ['conversations', projectId],
    queryFn: () => chatService.listConversations(projectId!),
    enabled: !!projectId,
  });

  // Navigate to first conversation if none selected
  useEffect(() => {
    if (!conversationId && conversations.length > 0) {
      navigate(`/projects/${projectId}/chat/${conversations[0].id}`, { replace: true });
    }
  }, [conversationId, conversations, navigate, projectId]);

  const activeConversation = conversations.find((c) => c.id === conversationId);

  // 3. Fetch Messages
  const { data: messages = [], isLoading: isMessagesLoading } = useQuery({
    queryKey: ['messages', conversationId],
    queryFn: () => chatService.getMessages(conversationId!),
    enabled: !!conversationId,
  });

  // Auto scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // Create Conversation
  const createConvMutation = useMutation({
    mutationFn: (title?: string) => chatService.createConversation(projectId!, { title }),
    onSuccess: (newConv) => {
      queryClient.invalidateQueries({ queryKey: ['conversations', projectId] });
      navigate(`/projects/${projectId}/chat/${newConv.id}`);
      toast.success('Đã tạo cuộc trò chuyện mới.');
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Rename Conversation
  const renameConvMutation = useMutation({
    mutationFn: ({ id, title }: { id: string; title: string }) =>
      chatService.renameConversation(id, { title }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', projectId] });
      setIsRenameOpen(false);
      toast.success('Đã cập nhật tiêu đề.');
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Delete Conversation
  const deleteConvMutation = useMutation({
    mutationFn: (id: string) => chatService.deleteConversation(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', projectId] });
      toast.success('Đã xóa cuộc trò chuyện.');
      navigate(`/projects/${projectId}/chat`);
    },
    onError: (err) => {
      toast.error(getErrorMessage(err).message);
    },
  });

  // Ask Question
  const askMutation = useMutation({
    mutationFn: async ({ convId, q, k }: { convId: string; q: string; k: number }) => {
      return chatService.ask(convId, { question: q, topK: k });
    },
  });

  const handleSend = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    const q = question.trim();
    if (!q || isSending || askMutation.isPending || !isRepoReady) return;

    try {
      setIsSending(true);
      let targetConvId = conversationId;

      // 1. Create conversation first if user started from /chat without an existing ID
      if (!targetConvId) {
        const created = await chatService.createConversation(projectId!, {});
        targetConvId = created.id;
        queryClient.invalidateQueries({ queryKey: ['conversations', projectId] });
      }

      // 2. Clear input before query
      setQuestion('');

      // 3. Ask question via mutateAsync (sequential execution prevents race conditions)
      const result = await askMutation.mutateAsync({ convId: targetConvId, q, k: topK });

      // 4. Invalidate queries for the specific active conversation
      queryClient.invalidateQueries({ queryKey: ['messages', targetConvId] });
      queryClient.invalidateQueries({ queryKey: ['conversations', projectId] });

      // 5. Update route URL if was newly created (after answer arrives)
      if (!conversationId) {
        navigate(`/projects/${projectId}/chat/${targetConvId}`, { replace: true });
      }

      const assistantMsg = result.assistantMessage || result.assistant;
      if (assistantMsg?.citations && assistantMsg.citations.length > 0) {
        setSelectedCitation(assistantMsg.citations[0]);
      }
    } catch (err: any) {
      if (err?.code !== 'ERR_CANCELED' && err?.name !== 'CanceledError') {
        const errInfo = getErrorMessage(err);
        if (errInfo.message) {
          toast.error(errInfo.message);
        }
      }
    } finally {
      setIsSending(false);
    }
  };

  const handleCitationClick = (citation: CitationDto) => {
    setSelectedCitation(citation);
    setIsDrawerOpen(true);
  };

  const latestAssistantMessage = [...messages].reverse().find((m) => m.role === 'assistant');
  const latestCitations = latestAssistantMessage?.citations || [];

  const filteredConversations = conversations.filter((c) =>
    c.title.toLowerCase().includes(searchConvQuery.toLowerCase())
  );

  return (
    <>
      <Header
        breadcrumbs={[
          { label: 'Projects', href: '/projects' },
          { label: project?.name || 'Project', href: `/projects/${projectId}` },
          { label: 'Chat' },
        ]}
      />

      <div className="pt-14 flex flex-1 h-[calc(100vh-3.5rem)] overflow-hidden bg-background text-foreground">
        {/* Sidebar: Conversation History */}
        <aside className="w-64 bg-card border-r border-border flex flex-col justify-between shrink-0 hidden md:flex select-none">
          {/* Top Actions */}
          <div className="p-3 flex flex-col gap-2 border-b border-border">
            <button
              type="button"
              onClick={() => createConvMutation.mutate(undefined)}
              disabled={createConvMutation.isPending}
              className="w-full h-8 px-2.5 rounded-md bg-foreground text-background text-xs font-medium flex items-center justify-between hover:bg-foreground/90 transition-all shadow-xs"
            >
              <div className="flex items-center gap-1.5">
                <Plus className="w-3.5 h-3.5" />
                <span>New Chat</span>
              </div>
              <span className="text-[10px] text-background/70 font-mono">⌘N</span>
            </button>
            <div className="relative">
              <Search className="w-3 h-3 absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none" />
              <input
                type="text"
                placeholder="Search chats..."
                value={searchConvQuery}
                onChange={(e) => setSearchConvQuery(e.target.value)}
                className="w-full bg-background border border-border rounded-md pl-7 pr-2 py-1 text-xs text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-primary"
              />
            </div>
          </div>

          {/* Conversations List */}
          <div className="flex-1 overflow-y-auto p-2 flex flex-col gap-0.5">
            {isConvsLoading ? (
              <div className="p-4 text-center text-xs text-muted-foreground">Loading...</div>
            ) : filteredConversations.length === 0 ? (
              <div className="p-6 text-center text-xs text-muted-foreground">
                No conversations yet.
              </div>
            ) : (
              filteredConversations.map((conv) => {
                const isActive = conv.id === conversationId;
                return (
                  <div
                    key={conv.id}
                    onClick={() => navigate(`/projects/${projectId}/chat/${conv.id}`)}
                    className={`group relative flex items-center justify-between p-2 rounded-md cursor-pointer transition-colors text-xs ${
                      isActive
                        ? 'bg-accent text-accent-foreground font-medium'
                        : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground'
                    }`}
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      <MessageSquare className="w-3.5 h-3.5 shrink-0 text-muted-foreground" />
                      <span className="truncate">{conv.title}</span>
                    </div>

                    <div className="flex items-center gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity">
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          setNewTitle(conv.title);
                          setIsRenameOpen(true);
                        }}
                        className="p-1 hover:text-foreground text-muted-foreground transition-colors"
                        title="Rename"
                      >
                        <Edit2 className="w-3 h-3" />
                      </button>
                      <button
                        type="button"
                        onClick={async (e) => {
                          e.stopPropagation();
                          const ok = await confirm({
                            title: 'Xóa cuộc hội thoại?',
                            description: `Bạn có chắc muốn xóa cuộc hội thoại "${conv.title}"? Toàn bộ lịch sử tin nhắn và trích dẫn sẽ bị xóa vĩnh viễn.`,
                            confirmText: 'Xóa hội thoại',
                            variant: 'danger',
                            iconType: 'trash',
                          });
                          if (ok) {
                            deleteConvMutation.mutate(conv.id);
                          }
                        }}
                        className="p-1 hover:text-destructive text-muted-foreground transition-colors"
                        title="Delete"
                      >
                        <Trash2 className="w-3 h-3" />
                      </button>
                    </div>
                  </div>
                );
              })
            )}
          </div>

          {/* Active Repo Info */}
          {activeRepo && (
            <div className="p-3 bg-card border-t border-border text-xs">
              <span className="text-[10px] text-muted-foreground uppercase font-medium tracking-wider">Indexed Repo</span>
              <div className="text-foreground font-medium truncate mt-0.5">{getRepoName(activeRepo.repoUrl)}</div>
              <div className="text-muted-foreground text-[11px] font-mono">{activeRepo.ref || 'main'}</div>
            </div>
          )}
        </aside>

        {/* Main Chat Panel */}
        <main id="main-content" className="flex-1 flex flex-col h-full min-w-0 bg-background">
          {/* Subheader */}
          <div className="h-12 px-6 flex items-center justify-between border-b border-border shrink-0 bg-background">
            <div className="flex items-center gap-2 min-w-0">
              <span className="text-xs font-semibold text-foreground truncate">
                {activeConversation?.title || 'Chat'}
              </span>
              {activeConversation && (
                <button
                  type="button"
                  onClick={() => {
                    setNewTitle(activeConversation.title);
                    setIsRenameOpen(true);
                  }}
                  className="p-1 rounded text-muted-foreground hover:text-foreground transition-colors"
                  title="Rename title"
                >
                  <Edit2 className="w-3 h-3" />
                </button>
              )}
            </div>

            {/* Controls */}
            <div className="flex items-center gap-2">
              {/* Top-K Selector */}
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setIsTopKOpen(!isTopKOpen)}
                  className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-card text-muted-foreground hover:text-foreground font-mono text-[11px] border border-border"
                  title="Retrieval Depth"
                >
                  <SlidersHorizontal className="w-3 h-3" />
                  <span>Top-{topK}</span>
                </button>

                {isTopKOpen && (
                  <div className="absolute right-0 top-full mt-1.5 w-48 p-3 rounded-lg bg-popover shadow-md border border-border z-30 flex flex-col gap-2">
                    <div className="flex justify-between text-xs">
                      <span>Top-K Chunks</span>
                      <span className="font-semibold">{topK}</span>
                    </div>
                    <input
                      type="range"
                      min={3}
                      max={20}
                      value={topK}
                      onChange={(e) => setTopK(Number(e.target.value))}
                      className="w-full accent-foreground cursor-pointer"
                    />
                  </div>
                )}
              </div>

              {/* Code Explorer Link */}
              <button
                type="button"
                onClick={() => navigate(`/projects/${projectId}/explorer`)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-card hover:bg-accent text-foreground text-xs border border-border transition-colors"
              >
                <Code2 className="w-3.5 h-3.5 text-muted-foreground" />
                <span className="hidden sm:inline">Code Explorer</span>
              </button>

              {/* Citations Button */}
              {latestCitations.length > 0 && (
                <button
                  type="button"
                  onClick={() => {
                    setSelectedCitation(latestCitations[0]);
                    setIsDrawerOpen(!isDrawerOpen);
                  }}
                  className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-foreground text-background text-xs font-medium shadow-xs"
                >
                  <BookOpen className="w-3 h-3" />
                  <span>Citations ({latestCitations.length})</span>
                </button>
              )}
            </div>
          </div>

          {/* Indexing Pipeline Banner */}
          {activeRepo && !isRepoReady && (
            <div className="bg-muted/40 border-b border-border px-4 py-3 shrink-0">
              <div className="max-w-4xl mx-auto flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-2 text-xs text-amber-500 font-medium">
                    <Loader2 className="w-3.5 h-3.5 animate-spin shrink-0" />
                    <span>Đang tự động index repository: <strong className="text-foreground font-mono">{getRepoName(activeRepo.repoUrl)}</strong></span>
                  </div>
                  <IndexingPipelineStepper repo={activeRepo} />
                </div>
                <button
                  type="button"
                  onClick={() => navigate(`/projects/${projectId}`)}
                  className="text-xs text-muted-foreground hover:text-foreground underline shrink-0 sm:self-center"
                >
                  Chi tiết Project
                </button>
              </div>
            </div>
          )}

          {!activeRepo && (
            <div className="bg-amber-500/10 border-b border-amber-500/20 px-6 py-2.5 flex items-center justify-between text-xs text-amber-500 shrink-0">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                <span>Dự án chưa có repository nào. Hãy import một repository GitHub để bắt đầu chat.</span>
              </div>
              <button
                type="button"
                onClick={() => navigate(`/projects/${projectId}`)}
                className="underline text-xs font-medium"
              >
                Import ngay
              </button>
            </div>
          )}

          {/* Messages Container */}
          <div className="flex-1 overflow-y-auto px-4 md:px-8 py-6 flex flex-col gap-5 max-w-4xl w-full mx-auto">
            {isMessagesLoading ? (
              <div className="flex-1 flex items-center justify-center">
                <Loader2 className="w-5 h-5 animate-spin text-muted-foreground" />
              </div>
            ) : messages.length === 0 ? (
              <div className="flex-1 flex flex-col items-center justify-center text-center max-w-md mx-auto gap-4 my-auto">
                <div className="w-10 h-10 rounded-lg bg-card border border-border flex items-center justify-center text-muted-foreground">
                  <Sparkles className="w-5 h-5" />
                </div>
                <div>
                  <h3 className="text-sm font-semibold text-foreground">Codebase Assistant</h3>
                  <p className="text-xs text-muted-foreground mt-1">
                    Ask questions grounded directly in your source code repository with verifiable citations.
                  </p>
                </div>
                <div className="flex flex-col gap-2 w-full text-left text-xs mt-2">
                  {[
                    'Dự án này làm gì và có những cấu trúc module chính nào?',
                    'Luồng xác thực token và xử lý phân quyền được cấu hình ở đâu?',
                    'Mô tả các API contracts và luồng tương tác database chính?',
                  ].map((sample, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => setQuestion(sample)}
                      className="p-2.5 rounded-md bg-card hover:bg-accent border border-border text-muted-foreground hover:text-foreground transition-colors text-left"
                    >
                      {sample}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              messages.map((msg) => (
                <MessageItem
                  key={msg.id}
                  message={msg}
                  onCitationClick={handleCitationClick}
                  userEmail={user?.email}
                  projectId={projectId}
                />
              ))
            )}

            {/* In-progress AI Thinking indicator (Lucian Element #078 / #083) */}
            {(isSending || askMutation.isPending) && <AiThinkingIndicator />}

            <div ref={messagesEndRef} />
          </div>

          {/* Input Form Bar */}
          <div className="p-4 border-t border-border bg-background shrink-0">
            <div className="max-w-4xl mx-auto flex flex-col gap-1.5">
              <form onSubmit={handleSend} className="relative flex items-center">
                <textarea
                  rows={2}
                  disabled={!isRepoReady || isSending || askMutation.isPending}
                  value={question}
                  onChange={(e) => setQuestion(e.target.value)}
                  aria-label="Câu hỏi tra cứu codebase"
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      handleSend();
                    }
                  }}
                  placeholder={
                    isRepoReady
                      ? 'Ask a question about the codebase... (Press Enter to send, Shift+Enter for newline)'
                      : 'Repository is indexing, please wait...'
                  }
                  className="w-full pl-3.5 pr-12 py-2.5 rounded-lg bg-card text-foreground placeholder:text-muted-foreground text-xs sm:text-sm border border-border focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:border-primary resize-none disabled:opacity-50"
                />

                <button
                  type="submit"
                  disabled={!question.trim() || isSending || askMutation.isPending || !isRepoReady}
                  className="group absolute right-2.5 p-2 rounded-lg bg-primary hover:bg-primary/90 text-primary-foreground disabled:opacity-30 transition-all duration-200 shadow-md focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none min-h-[32px] min-w-[32px] flex items-center justify-center cursor-pointer disabled:cursor-not-allowed active:scale-95"
                  title="Gửi câu hỏi (Enter)"
                  aria-label="Gửi câu hỏi"
                >
                  {(isSending || askMutation.isPending) ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <Send className="w-4 h-4 transition-transform duration-200 group-hover:translate-x-0.5 group-hover:-translate-y-0.5" />
                  )}
                </button>
              </form>

              <div className="flex items-center justify-between text-[11px] text-muted-foreground px-1">
                <span>Top-{topK} context chunks</span>
                <span>Deterministic citations with file &amp; line ranges</span>
              </div>
            </div>
          </div>
        </main>

        {/* Citations Drawer */}
        <CitationDrawer
          isOpen={isDrawerOpen}
          onClose={() => setIsDrawerOpen(false)}
          citation={selectedCitation}
          repositoryId={activeRepo?.id}
          projectId={projectId}
        />

        {/* Rename Conversation Modal */}
        {isRenameOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 animate-in fade-in duration-150">
            <div className="w-full max-w-sm rounded-lg bg-card border border-border shadow-2xl p-5 flex flex-col gap-4 animate-in zoom-in-95 duration-150">
              <div className="flex items-center justify-between pb-3 border-b border-border">
                <h3 className="text-sm font-semibold text-foreground">Rename Conversation</h3>
                <button
                  type="button"
                  onClick={() => setIsRenameOpen(false)}
                  aria-label="Close dialog"
                  className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              </div>

              <input
                type="text"
                required
                autoFocus
                value={newTitle}
                onChange={(e) => setNewTitle(e.target.value)}
                className="w-full px-3 py-1.5 rounded-md bg-muted text-foreground text-xs border border-border focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary focus-visible:border-primary transition-colors"
              />
              <div className="flex items-center justify-end gap-2 pt-2 border-t border-border">
                <button
                  type="button"
                  onClick={() => setIsRenameOpen(false)}
                  className="px-3 py-1.5 rounded-md text-foreground hover:bg-accent text-xs transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  disabled={renameConvMutation.isPending || !newTitle.trim()}
                  onClick={() => {
                    if (conversationId && newTitle.trim()) {
                      renameConvMutation.mutate({ id: conversationId, title: newTitle.trim() });
                    }
                  }}
                  className="px-3.5 py-1.5 rounded-md bg-primary text-primary-foreground text-xs font-semibold hover:bg-primary/90 transition-all disabled:opacity-50 shadow-xs"
                >
                  Save
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </>
  );
};
