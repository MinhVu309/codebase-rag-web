import { create } from 'zustand';
import { api } from '@/services/api';
import type { UserDto, LoginRequest, RegisterRequest } from '@/types/api';

interface AuthState {
  user: UserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  checkAuth: () => Promise<void>;
  login: (data: LoginRequest) => Promise<UserDto>;
  register: (data: RegisterRequest) => Promise<UserDto>;
  logout: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set) => {
  // Listen for session expiry from API interceptor
  if (typeof window !== 'undefined') {
    window.addEventListener('auth:session-expired', () => {
      set({ user: null, isAuthenticated: false });
    });
  }

  return {
    user: null,
    isAuthenticated: false,
    isLoading: true,

    checkAuth: async () => {
      try {
        set({ isLoading: true });
        const res = await api.get<UserDto>('/auth/me');
        set({ user: res.data, isAuthenticated: true, isLoading: false });
      } catch {
        set({ user: null, isAuthenticated: false, isLoading: false });
      }
    },

    login: async (data: LoginRequest) => {
      const res = await api.post<UserDto>('/auth/login', data);
      set({ user: res.data, isAuthenticated: true });
      return res.data;
    },

    register: async (data: RegisterRequest) => {
      const res = await api.post<UserDto>('/auth/register', data);
      set({ user: res.data, isAuthenticated: true });
      return res.data;
    },

    logout: async () => {
      try {
        await api.post('/auth/logout');
      } finally {
        set({ user: null, isAuthenticated: false });
      }
    },
  };
});
