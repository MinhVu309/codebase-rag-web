import { create } from 'zustand';

export type ThemeMode = 'dark' | 'light' | 'system';

interface ThemeState {
  theme: ThemeMode;
  isDark: boolean;
  setTheme: (theme: ThemeMode) => void;
  toggleTheme: () => void;
}

const getSystemDark = () => {
  if (typeof window === 'undefined') return true;
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
};

const applyThemeToDOM = (theme: ThemeMode): boolean => {
  if (typeof window === 'undefined') return true;

  const isDark = theme === 'system' ? getSystemDark() : theme === 'dark';
  document.documentElement.classList.toggle('dark', isDark);
  return isDark;
};

export const useThemeStore = create<ThemeState>((set, get) => {
  const storedTheme = (typeof window !== 'undefined'
    ? (localStorage.getItem('theme') as ThemeMode) || 'dark'
    : 'dark') as ThemeMode;

  const initialIsDark = applyThemeToDOM(storedTheme);

  // Listen to system changes if theme is system
  if (typeof window !== 'undefined') {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (get().theme === 'system') {
        const isDark = applyThemeToDOM('system');
        set({ isDark });
      }
    });
  }

  return {
    theme: storedTheme,
    isDark: initialIsDark,
    setTheme: (newTheme: ThemeMode) => {
      if (typeof window !== 'undefined') {
        localStorage.setItem('theme', newTheme);
      }
      const isDark = applyThemeToDOM(newTheme);
      set({ theme: newTheme, isDark });
    },
    toggleTheme: () => {
      const current = get().isDark;
      const nextTheme = current ? 'light' : 'dark';
      if (typeof window !== 'undefined') {
        localStorage.setItem('theme', nextTheme);
      }
      const isDark = applyThemeToDOM(nextTheme);
      set({ theme: nextTheme, isDark });
    },
  };
});
