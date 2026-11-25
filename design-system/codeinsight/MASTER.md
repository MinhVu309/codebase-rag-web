# CodeInsight Design System — MASTER

> **Global Source of Truth for CodeInsight UI/UX**  
> Generated and aligned with the `ui-ux-pro-max` (Linear / Cursor Minimalist Craftsmanship) specification.

---

## 1. Visual Direction & Style

- **Archetype:** Developer Tool / High-Craft Engineering SaaS (Linear, Raycast, Cursor).
- **Core Principle:** Clarity over decoration. Content-first, high information density without visual noise. Zero fake telemetry (no fake SOC2 badges, no fake latency tickers, no synthetic similarity bars).
- **Surface Elevation:** 1px hairline borders (`border-border`) over glowing neon borders or box-in-a-box nested containers.

---

## 2. Design Tokens

### Color Palette (CSS Custom Properties RGB)

| Token | Dark Mode (`.dark`) | Light Mode | Semantic Purpose |
|---|---|---|---|
| `--background` | `240 10% 3.9%` (`#09090b`) | `0 0% 100%` (`#ffffff`) | App canvas root |
| `--foreground` | `0 0% 98%` (`#fafafa`) | `240 10% 3.9%` (`#09090b`) | Primary readable text |
| `--card` | `240 10% 4.9%` (`#0c0c0e`) | `0 0% 99%` (`#fcfcfc`) | Surface containers & panels |
| `--border` | `240 3.7% 15.9%` (`#27272a`) | `240 5.9% 90%` (`#e4e4e7`) | 1px hairline structural dividers |
| `--muted` | `240 3.7% 15.9%` (`#27272a`) | `240 4.8% 95.9%` (`#f4f4f5`) | Subdued interactive elements |
| `--muted-foreground` | `240 5% 64.9%` (`#a1a1aa`) | `240 3.8% 46.1%` (`#71717a`) | Secondary text (Contrast >= 4.5:1) |
| `--accent` | `240 3.7% 15.9%` | `240 4.8% 95.9%` | Hover & active row highlights |
| `--primary` | `238 84% 67%` (`#6366f1`) | `239 84% 59%` (`#4f46e5`) | Primary action & focus indicator |
| `--secondary` | `160 84% 39%` (`#10b981`) | `160 84% 35%` | Ready status, active index |
| `--destructive` | `0 84.2% 60.2%` (`#ef4444`) | `0 84.2% 60.2%` | Destructive actions & error alerts |

---

## 3. Typography Hierarchy

- **UI Font:** `Inter`, system sans-serif (`font-sans`), `-apple-system`, `BlinkMacSystemFont`.
- **Code & Metadata Font:** `JetBrains Mono`, `Fira Code`, `ui-monospace`, `monospace`.
- **Sizes:**
  - Page Titles: `text-xl font-semibold tracking-tight`
  - Section Headers: `text-sm font-semibold text-foreground`
  - Body Text: `text-xs sm:text-sm leading-relaxed text-foreground`
  - Metadata & Badges: `text-[11px] font-mono text-muted-foreground`
- **Rule:** Body text is never smaller than 12px (`text-xs`). `text-[10px]` is strictly reserved for uppercase category labels and status indicators.

---

## 4. Spacing & Density

- **Scale:** 8px base grid (`p-2`, `p-3`, `p-4`, `p-6`).
- **Dashboard Density:** High density with clean breathing room (gap 12px–16px between cards).
- **Touch Target:** Minimum interactive touch target `32px × 32px` on desktop with expansion for mobile tap targets (`44px × 44px`).

---

## 5. UI/UX Pro Max 10-Priority Enforcement

1. **Accessibility (CRITICAL):**
   - Minimum text contrast 4.5:1.
   - All icon-only buttons have an explicit `aria-label`.
   - Never remove focus rings: every interactive element has `focus-visible:ring-1 focus-visible:ring-primary focus-visible:outline-none`.
   - Skip to main content link on root layout.
2. **Touch & Interaction (CRITICAL):**
   - Transitions between 100ms–200ms (`duration-150 ease-out`). Never instant 0ms state changes.
   - Hover and active states are distinct.
3. **Performance (HIGH):**
   - Prevent layout shift (reserve fixed heights for headers, skeleton loaders for fetching queries).
4. **Style Selection (HIGH):**
   - SVG icons from `lucide-react`. Never use raw emojis as UI icons.
5. **Layout & Responsive (HIGH):**
   - Responsive sidebar with mobile slide-in overlay drawer and backdrop blur.
   - No horizontal scrollbars on `<main>`.
6. **Forms & Feedback (MEDIUM):**
   - Visible input labels.
   - Inline field error display with `aria-invalid="true"`.
   - Loading spinners inside submit buttons (`disabled:opacity-50`).
7. **Animation (MEDIUM):**
   - Respect `prefers-reduced-motion`.
8. **Navigation Patterns (HIGH):**
   - Breadcrumbs on every page.
   - Deep linking support (e.g. `?file=...&lineStart=...`).
