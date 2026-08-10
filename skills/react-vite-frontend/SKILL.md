---
name: react-vite-frontend
description: Official-source development guidance for the React 19, Vite, TypeScript, CSS, WebView2 and test stack in src/OnlyRag.Web.
---

# React/Vite Frontend Skill

Use this skill for changes under `src/OnlyRag.Web`. The frontend is a React 19 + TypeScript SPA bundled by Vite and hosted by the WPF WebView2 shell. It uses CSS files and custom properties; Tailwind CSS and a `tailwind.config.js` are not part of this repository.

## Official sources

Use primary documentation only:

- React: https://react.dev/
- Vite: https://vite.dev/guide/
- TypeScript: https://www.typescriptlang.org/docs/
- MDN CSS: https://developer.mozilla.org/en-US/docs/Web/CSS
- MDN ARIA: https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA
- Lucide: https://lucide.dev/guide/
- Vitest: https://vitest.dev/guide/
- Playwright: https://playwright.dev/docs/intro
- Microsoft WebView2: https://learn.microsoft.com/en-us/microsoft-edge/webview2/

Repository documentation is implementation context, not a replacement for the official API references.

## Repository structure

- `src/App.tsx`: application shell, navigation and lazy-loaded sections.
- `src/components/`: feature UI and presentation components.
- `src/hooks/`, `src/components/**/use*.ts`: async controllers and reusable state.
- `src/context/`: React Query, SignalR and theme providers.
- `src/api.ts`, `src/apiClient.ts`, `src/apiTypes/`: backend client and shared wire types.
- `src/styles/` and `src/styles.css`: global tokens, themes and feature styles.
- `src/test/` and `*.test.tsx`: Vitest/Testing Library tests.
- `e2e/`: Playwright backend contract specifications.

## Integration rules

- Call the in-process backend through the existing HTTP client and SignalR services. Do not create a second bridge or hard-code a backend port.
- Keep TypeScript request/response types aligned with `src/OnlyRag.Core` contracts and the endpoint mapping. Update tests when a contract changes.
- Use the existing query keys, invalidation helpers and SignalR event names. Preserve polling fallbacks for long-running jobs.
- Keep feature logic in hooks/controllers and keep render components focused on presentation.
- Represent loading, empty, error, offline and progress states explicitly. Do not silently turn a failed request into an empty success state.

## UI, accessibility and CSS

- Reuse the existing CSS custom properties, theme selectors and component patterns before adding new global selectors.
- Every interactive control needs an accessible name, keyboard behavior and a visible focus state. Use `aria-pressed`, `aria-expanded`, `aria-describedby` and live regions where their semantics apply.
- Respect `prefers-reduced-motion` and do not rely on color alone for status or validation.
- Use Lucide icons with an accessible label or mark decorative icons as hidden.
- Preserve responsive behavior in `responsive-app.css`; test narrow layouts when changing navigation or dialogs.

## Commands

Run from `src/OnlyRag.Web` and keep commands serial:

```powershell
npm run typecheck
npm run lint
npm run format:check
npm run test:unit
npm run build
```

Run Playwright only when the change affects an e2e flow:

```powershell
npm run test:e2e
```

The repository-level wrappers are the preferred agent entrypoints:

```powershell
pwsh .\scripts\Format-Code.ps1 -CheckOnly
pwsh .\scripts\Lint-Code.ps1 -Configuration Release
pwsh .\scripts\Invoke-Gate.ps1 -Fast
```
