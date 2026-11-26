import React from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';

export const AppLayout: React.FC = () => {
  return (
    <div className="min-h-screen bg-background text-foreground flex flex-col md:flex-row">
      {/* Accessibility: Skip to Main Content Link */}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:px-3 focus:py-1.5 focus:bg-foreground focus:text-background focus:rounded-md focus:shadow-md focus:text-xs focus:font-medium focus:outline-none focus:ring-2 focus:ring-primary"
      >
        Skip to main content
      </a>

      <Sidebar />

      <div className="pl-0 md:pl-64 flex flex-col flex-1 min-h-screen min-w-0">
        <Outlet />
      </div>
    </div>
  );
};
