# OnlyRag Audit Tracker

## Preflight

- Repository: `D:\GITHUB\OnlyRag`
- Branch: `main`
- `git status --short`: clean before this audit.
- `AUDIT_TRACKER.md`: absent before this audit.
- Audit date: 2026-05-24.

## Findings

| ID | Severity | Area | Status | Finding | Planned resolution |
| --- | --- | --- | --- | --- | --- |
| AUD-001 | High | Initial setup wizard | Fixed in current work | The startup wizard checked chat and embedding defaults, but did not check the translation model. A saved translation model missing from Ollama could pass the initial setup gate. | Validate chat, embedding, and translation defaults independently; distinguish unconfigured defaults from saved defaults that are not installed. |
| AUD-002 | High | OCR GPU setup | Fixed in current work | OCR GPU auto-enable ran only during the initial app load. If OCR provisioning completed later, usable GPU support did not automatically update OCR settings. | After OCR provisioning reaches a configured state, refresh diagnostics and call the existing auto-enable endpoint when GPU OCR is usable. |
| AUD-003 | Medium | Settings OCR provisioning | Fixed in current work | The Settings provisioning action refreshed dependency status but did not reload diagnostics/OCR settings after a completed provisioning result. | Re-read diagnostics and OCR settings after configured OCR status, applying auto-enable GPU when supported. |
| AUD-004 | Medium | Web verification | Verified | Web checks require `src\OnlyRag.Web\node_modules`. The dependency directory was absent in the prior tracker state, so checks needed dependency restore first. | Restored npm dependencies with `npm ci`; targeted and full frontend checks passed. |

## Verification Plan

- `Test-Path .\AUDIT_TRACKER.md`
- `Get-Content -Raw .\AUDIT_TRACKER.md`
- Frontend unit tests for startup model checks and OCR GPU post-provisioning.
- Frontend typecheck and lint when dependencies are available.
- `git status --short`
