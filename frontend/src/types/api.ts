export type IndexStatus = 'pending' | 'cloning' | 'parsing' | 'embedding' | 'ready' | 'failed';

export interface UserDto {
  id: string;
  email: string;
  createdAt: string;
}

export interface RegisterRequest {
  email?: string;
  password?: string;
}

export interface LoginRequest {
  email?: string;
  password?: string;
}

export interface ProjectSummaryDto {
  id: string;
  name: string;
  createdAt: string;
  repositoryCount: number;
}

export interface RepositoryDto {
  id: string;
  projectId: string;
  repoUrl: string;
  ref: string | null;
  status: IndexStatus;
  stage: string | null;
  filesDone: number;
  totalFiles: number;
  commitSha: string | null;
  isActive: boolean;
  stale?: boolean;
  errorCode: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ProjectDetailDto {
  id: string;
  name: string;
  createdAt: string;
  repositories: RepositoryDto[];
}

export interface CreateProjectRequest {
  name: string;
}

export interface UpdateProjectRequest {
  name: string;
}

export interface ImportRepositoryRequest {
  repoUrl: string;
  ref?: string | null;
}

export interface CodeFileDto {
  path: string;
  start: number;
  end: number;
  content: string;
  language: string | null;
  commitSha: string | null;
}

export interface ConversationSummaryDto {
  id: string;
  title: string;
  createdAt: string;
  lastActivityAt: string;
  messageCount: number;
}

export interface ConversationDto {
  id: string;
  projectId: string;
  title: string;
  createdAt: string;
}

export interface CreateConversationRequest {
  title?: string;
}

export interface UpdateConversationRequest {
  title: string;
}

export interface CitationDto {
  refId: number;
  filePath: string;
  symbol: string | null;
  symbolKind: string | null;
  startLine: number;
  endLine: number;
  snippet: string;
  score: number | null;
  repositoryId: string;
  commitSha: string | null;
}

export type MessageRole = 'user' | 'assistant';

export interface MessageDto {
  id: string;
  conversationId: string;
  role: MessageRole;
  content: string;
  citations: CitationDto[];
  latencyMs: number | null;
  errorCode: string | null;
  createdAt: string;
}

export interface AskRequest {
  question: string;
  topK?: number;
}

export interface AskResultDto {
  userMessage: MessageDto;
  assistantMessage: MessageDto;
  // Aliases for backward compatibility
  user?: MessageDto;
  assistant?: MessageDto;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  errors?: Record<string, string[]>;
}
