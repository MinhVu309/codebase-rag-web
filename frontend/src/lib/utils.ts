import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';
import type { ProblemDetails } from '@/types/api';
import { isAxiosError } from 'axios';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatTimeAgo(isoString: string): string {
  try {
    const date = new Date(isoString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffSec = Math.floor(diffMs / 1000);
    const diffMin = Math.floor(diffSec / 60);
    const diffHour = Math.floor(diffMin / 60);
    const diffDay = Math.floor(diffHour / 24);

    if (diffSec < 60) return 'Vừa xong';
    if (diffMin < 60) return `${diffMin}m ago`;
    if (diffHour < 24) return `${diffHour}h ago`;
    if (diffDay < 7) return `${diffDay}d ago`;
    return date.toLocaleDateString('vi-VN', { month: 'short', day: 'numeric' });
  } catch {
    return isoString;
  }
}

export function formatDate(isoString: string): string {
  try {
    return new Date(isoString).toLocaleString('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return isoString;
  }
}

export function getErrorMessage(error: unknown, fallback = 'Có lỗi xảy ra. Vui lòng thử lại.'): {
  message: string;
  code?: string;
  retryAfter?: number;
  validationErrors?: Record<string, string[]>;
} {
  if (isAxiosError(error)) {
    if (error.code === 'ERR_CANCELED' || error.name === 'CanceledError') {
      return { message: '' };
    }

    const retryHeader = error.response?.headers?.['retry-after'];
    const retryAfter = retryHeader ? parseInt(retryHeader, 10) : undefined;

    const data = error.response?.data as ProblemDetails | undefined;
    if (data) {
      const code = data.code;
      const validationErrors = data.errors;
      let message = data.detail || data.title || fallback;

      // Map stable backend error codes to helpful Vietnamese messages
      switch (code) {
        case 'RATE_LIMITED':
          message = retryAfter
            ? `Bạn gửi yêu cầu quá nhanh. Vui lòng thử lại sau ${retryAfter} giây.`
            : 'Bạn thao tác quá nhanh. Vui lòng chờ giây lát.';
          break;
        case 'IMPORT_IN_PROGRESS':
          message = 'Bạn đang có repository đang được index. Vui lòng chờ index xong rồi tiếp tục import.';
          break;
        case 'INDEX_NOT_READY':
          message = 'Repository chưa index xong. Vui lòng chờ trạng thái "ready" rồi thử lại.';
          break;
        case 'NO_REPOSITORY':
          message = 'Dự án chưa có repository nào được import.';
          break;
        case 'REPOSITORY_FAILED':
          message = 'Repository đã index thất bại. Hãy xóa và import lại.';
          break;
        case 'INVALID_REPO':
        case 'INVALID_REQUEST':
          message = 'URL repository hoặc yêu cầu không hợp lệ.';
          break;
        case 'REPO_TOO_LARGE':
          message = 'Repository vượt quá dung lượng tối đa cho phép.';
          break;
        case 'FILE_NOT_FOUND':
          message = 'File không còn tồn tại trong bản đã index.';
          break;
        case 'INDEX_NOT_FOUND':
          message = 'Chỉ mục của repository không còn tồn tại trên RAG.';
          break;
        case 'INVALID_RANGE':
          message = 'Khoảng dòng không hợp lệ (tối đa 2000 dòng).';
          break;
        case 'LLM_ERROR':
          message = 'Hệ thống AI gặp lỗi tạm thời. Vui lòng thử lại.';
          break;
        case 'RAG_UNAVAILABLE':
          message = 'Hệ thống AI tạm thời không khả dụng.';
          break;
        case 'TIMEOUT':
          message = 'Hệ thống AI phản hồi quá thời gian cho phép.';
          break;
        case 'REQUEST_TOO_LARGE':
          message = 'Dung lượng yêu cầu vượt quá giới hạn 64KB.';
          break;
      }

      return { message, code, retryAfter, validationErrors };
    }

    if (error.code === 'ECONNABORTED' || error.message.includes('timeout')) {
      return { message: 'Yêu cầu quá hạn thời gian chờ. Vui lòng thử lại.' };
    }

    if (!error.response) {
      return { message: 'Không thể kết nối đến máy chủ. Kiểm tra kết nối mạng.' };
    }
  }

  return { message: fallback };
}
