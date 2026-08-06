---
name: react-vite-frontend
description: Technical skill for developing the React 19, Vite, TypeScript, and Tailwind CSS frontend layer of OnlyRag, including WebView2 bridge integration, Vitest component testing, and Playwright e2e tests.
---

# React 19, Vite & Tailwind CSS Frontend Skill

This skill provides guidelines and standards for developing the user interface in `src/OnlyRag.Web`.

## 1. Official Documentation Sources

- **React 19 Documentation**: [react.dev](https://react.dev/)
- **Vite Guide**: [vite.dev/guide](https://vite.dev/guide/)
- **TypeScript Handbook**: [typescriptlang.org/docs](https://www.typescriptlang.org/docs/)
- **Tailwind CSS Documentation**: [tailwindcss.com/docs](https://tailwindcss.com/docs)
- **Lucide Icons**: [lucide.dev](https://lucide.dev/)
- **Vitest Guide**: [vitest.dev/guide](https://vitest.dev/guide/)
- **Playwright Testing**: [playwright.dev/docs/intro](https://playwright.dev/docs/intro)

## 2. Directory Structure & Key Files

Directory: [`src/OnlyRag.Web`](file:///d:/GITHUB/OnlyRag/src/OnlyRag.Web)

```
src/OnlyRag.Web/
├── src/
│   ├── api/          # API client & bridge interop types
│   ├── components/   # Modular React UI components (Sidebar, Topbar, Modals, Views)
│   ├── context/      # React contexts (Theme, Settings, Navigation, Active Job)
│   ├── hooks/        # Custom hooks (useDocuments, useChat, useImageGen, useJobPoller)
│   ├── types/        # TypeScript interfaces matching OnlyRag.Core backend DTOs
│   ├── App.tsx       # Root view switcher & layout shell
│   └── main.tsx      # Entry point
├── tests/            # Vitest unit/component tests & Playwright e2e specifications
├── package.json      # Dependencies and scripts
├── vite.config.ts    # Vite bundler configuration
└── tailwind.config.js# Styling tokens and theme extensions
```

## 3. Interop & Bridge Architecture

- **WebView2 Communication**: The frontend interacts with the in-process .NET backend via standard HTTP fetch requests sent to the backend host (or via window `chrome.webview` postMessage bridge where configured).
- **Development Server Override**: Setting `$env:ONLYRAG_WEB_DEV_SERVER = "http://127.0.0.1:5173"` enables Hot Module Reloading (HMR) inside the WPF WebView2 container.

## 4. UI Design System Guidelines

- **Theme & Palette**: Modern dark/light mode UI with HSL CSS variables and Tailwind utility classes.
- **Typography**: Clean, readable fonts with hierarchical scaling (`text-sm`, `text-base`, `text-lg`, `font-semibold`).
- **Feedback & States**: Every async action (document import, embedding generation, OCR run, image creation) must show loading spinners, progress bars, or status badges.
- **Accessibility & Identifiers**: Include unique, descriptive `id` and `aria-label` attributes on interactive elements to facilitate Playwright end-to-end testing and accessibility.

## 5. Standard Scripts & Checks

All commands run from `src/OnlyRag.Web` (or via root scripts):

```powershell
# Change directory
Set-Location .\src\OnlyRag.Web

# 1. Typecheck TypeScript
npm run typecheck

# 2. ESLint Check
npm run lint

# 3. Code Format Verification
npm run format:check

# 4. Unit / Component Tests (Vitest)
npm run test

# 5. Production Build Verification
npm run build
```

## 6. Development Rules

1. Do not introduce ad-hoc global CSS when Tailwind utilities or scoped CSS variables can be used.
2. Keep component responsibilities small and isolated. Extract complex logic into custom hooks under `src/hooks/`.
3. Strict TypeScript mode is enabled (`tsconfig.json`). Avoid `any` types; define exact DTO models matching `OnlyRag.Core`.
4. Avoid unhandled state promises in components; use try/catch blocks with user-visible toast notifications or alert banners.
