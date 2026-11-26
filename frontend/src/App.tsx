import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'sonner';

import { ProtectedRoute } from '@/components/auth/ProtectedRoute';
import { AppLayout } from '@/components/layout/AppLayout';

import { LoginPage } from '@/pages/auth/LoginPage';
import { RegisterPage } from '@/pages/auth/RegisterPage';
import { DashboardPage } from '@/pages/dashboard/DashboardPage';
import { ProjectsPage } from '@/pages/projects/ProjectsPage';
import { ProjectDetailPage } from '@/pages/projects/ProjectDetailPage';
import { ChatPage } from '@/pages/chat/ChatPage';
import { CodeExplorerPage } from '@/pages/explorer/CodeExplorerPage';
import { SettingsPage } from '@/pages/settings/SettingsPage';
import { useThemeStore } from '@/stores/themeStore';
import { ConfirmProvider } from '@/components/ui/ConfirmDialog';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      staleTime: 5000,
      retry: (failureCount, error: any) => {
        // Do not retry 401, 403, 404
        const status = error?.response?.status;
        if (status === 401 || status === 403 || status === 404 || status === 409) return false;
        return failureCount < 2;
      },
    },
  },
});

export const App: React.FC = () => {
  const { isDark } = useThemeStore();

  return (
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <BrowserRouter>
          <Routes>
            {/* Public Auth Routes */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />

            {/* Protected App Routes */}
            <Route element={<ProtectedRoute />}>
              <Route element={<AppLayout />}>
                <Route path="/" element={<Navigate to="/dashboard" replace />} />
                <Route path="/dashboard" element={<DashboardPage />} />
                <Route path="/projects" element={<ProjectsPage />} />
                <Route path="/projects/:projectId" element={<ProjectDetailPage />} />
                <Route path="/projects/:projectId/chat" element={<ChatPage />} />
                <Route path="/projects/:projectId/chat/:conversationId" element={<ChatPage />} />
                <Route path="/projects/:projectId/explorer" element={<CodeExplorerPage />} />
                <Route path="/settings" element={<SettingsPage />} />
              </Route>
            </Route>

            {/* Fallback */}
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </BrowserRouter>
        <Toaster position="top-right" richColors theme={isDark ? 'dark' : 'light'} />
      </ConfirmProvider>
    </QueryClientProvider>
  );
};

export default App;
