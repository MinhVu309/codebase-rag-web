import { api } from './api';
import type {
  ConversationSummaryDto,
  ConversationDto,
  CreateConversationRequest,
  UpdateConversationRequest,
  MessageDto,
  AskRequest,
  AskResultDto,
} from '@/types/api';

export const chatService = {
  async listConversations(projectId: string): Promise<ConversationSummaryDto[]> {
    const res = await api.get<ConversationSummaryDto[]>(`/projects/${projectId}/conversations`);
    return res.data;
  },

  async createConversation(projectId: string, data: CreateConversationRequest): Promise<ConversationDto> {
    const res = await api.post<ConversationDto>(`/projects/${projectId}/conversations`, data);
    return res.data;
  },

  async renameConversation(conversationId: string, data: UpdateConversationRequest): Promise<ConversationDto> {
    const res = await api.patch<ConversationDto>(`/conversations/${conversationId}`, data);
    return res.data;
  },

  async deleteConversation(conversationId: string): Promise<void> {
    await api.delete(`/conversations/${conversationId}`);
  },

  async getMessages(conversationId: string, limit = 200): Promise<MessageDto[]> {
    const res = await api.get<MessageDto[]>(`/conversations/${conversationId}/messages?limit=${limit}`);
    return res.data;
  },

  async ask(conversationId: string, data: AskRequest): Promise<AskResultDto> {
    // 75s timeout specifically for RAG query as documented in backend README
    const res = await api.post<AskResultDto>(`/conversations/${conversationId}/messages`, data, {
      timeout: 75000,
    });
    return res.data;
  },
};
