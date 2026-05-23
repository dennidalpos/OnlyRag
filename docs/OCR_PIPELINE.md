# OCR Pipeline

OnlyRag runs OCR inside the existing persistent document ingestion job. The UI never calls
PaddleOCR directly and remains responsive while the background worker processes one page at a time.

Implemented stages:

1. Queue document ingestion or forced OCR from the local job queue. The document UI exposes
   **Rileggi tutto con OCR**, which calls `POST /api/documents/{id}/ocr?force=true` to bypass the
   OCR cache explicitly.
2. For textual PDFs, use embedded text first unless forced OCR is requested.
3. For scanned PDF pages and images, prepare a page image through the isolated Python bridge.
4. Apply preprocessing in the bridge: RGB/grayscale normalization, light denoise, optional deskew
   when OpenCV is available, and PNG render output.
5. Build the OCR cache key from `pageHash + engineName + engineVersion + language + preprocessVersion`.
6. Reuse cached OCR text when available, unless the job was created with forced OCR.
7. Run PaddleOCR with a per-page timeout and at most two retries.
8. Save text, render path, cache key, bounding boxes JSON, confidence, status, and errors on
   `document_pages`; save reusable OCR output in `ocr_cache`.
9. Chunk OCR text into the existing RAG ingestion flow.
10. Save a job checkpoint after every page.

## Runtime Settings

OCR processing settings are persisted with existing keys and normalized by the backend:

- `ocr.language`: default OCR language.
- `ocr.maxRetries`: default `2`, clamped to `0..2`.
- `ocr.pageTimeoutSeconds`: default `180`, clamped to `15..600`.
- `ocr.lowConfidenceThreshold`: default `0.55`, clamped to `0.01..0.99`.

These runtime settings are separate from the PaddleOCR profile/model settings. Model context
recommendations shown in Settings are visual only and do not modify saved values automatically.

Bridge and prerequisites:

- Bridge script: `scripts\ocr\paddle_ocr_bridge.py`.
- Default CPU requirements: `scripts\ocr\requirements.txt` -> `requirements-cpu.txt`.
- Shared requirements: `scripts\ocr\requirements-common.txt`.
- NVIDIA requirements: `requirements-nvidia-cu129.txt`, `requirements-nvidia-cu126.txt`, and
  `requirements-nvidia-cu118.txt`.
- Supported Python versions: 3.10 through 3.13. The pinned PaddlePaddle runtime does not publish Windows wheels for Python 3.14.
- End-user setup: **Settings > Diagnostica > Configura OCR** prepares the local OCR environment when Python is available.
- Provisioning can be cancelled from **Settings > Diagnostica > Annulla OCR** while it is running.
  OnlyRag also applies a 45-minute upper bound; cancellation and timeout stop the active child process tree
  and leave a recoverable status so the user can retry **Configura OCR**.
- GPU setup: **Configura OCR** uses `auto` mode. It chooses NVIDIA only when `nvidia-smi`
  reports a driver compatible with the pinned CUDA 12.9, CUDA 12.6, or CUDA 11.8 PaddlePaddle GPU wheels;
  otherwise it installs CPU OCR and reports the fallback reason in Diagnostics. Provisioning removes both
  `paddlepaddle` and `paddlepaddle-gpu` before installing the selected wheel so CPU and GPU packages cannot mask each other.
- Developer bootstrap: `scripts\Bootstrap-Prerequisites.ps1` can prepare the same local OCR environment during repository setup; use `-SkipOcr` to skip it.
- Default OCR Python path: `%LOCALAPPDATA%\OnlyRag\ocr-python\.venv\Scripts\python.exe`.
- Override Python with `ONLYRAG_OCR_PYTHON`.
- Override bridge path with `ONLYRAG_OCR_BRIDGE`.
- Select `GPU` in OCR settings only after Diagnostics reports that `paddle_ocr_bridge.py --mode check --device gpu`
  is usable with `compiledWithCuda=true`, `cudaDeviceCount > 0`, and `activeDevice=gpu:0`. The backend rejects
  `PUT /api/settings/ocr` with `device=gpu` until that capability check passes. If a CPU-only runtime receives
  `device=gpu`, the bridge returns a clear configuration error instead of silently falling back.

PaddleOCR profile presets are device-specific:

| Profile | CPU recognition batch | GPU recognition batch |
|---|---:|---:|
| `fast` | 4 | 8 |
| `balanced` | 6 | 12 |
| `accurate` | 8 | 16 |

Quality-oriented settings such as DPI, detection thresholds, orientation, and unwarping remain identical between
CPU and GPU presets.

Settings Diagnostics also shows live local telemetry: CPU usage/logical processors, RAM total/free, system disk
total/free, NVIDIA GPU name/driver/utilization/VRAM when available, and OCR GPU compatibility state.

Supported OCR inputs:

- Images: `.png`, `.jpg`, `.jpeg`, `.tif`, `.tiff`, `.bmp`, `.gif`, and `.webp` when Pillow can
  open them.
- PDFs: scanned pages rendered by `pypdfium2` in the bridge.

Notes:

- PaddleOCR is not assumed to be installed. If unavailable, document ingestion returns a clear
  configuration error for pages that require OCR.
- The real PaddleOCR engine is not exercised by unit tests. Tests use a fake `IOcrEngine` for
  cache, retry, checkpoint, and ingestion behavior.
- The installer does not bundle OCR Python packages or PaddleOCR models. The supported delivery
  model is per-user provisioning from **Configura OCR** in Settings, which prepares and verifies
  `%LOCALAPPDATA%\OnlyRag\ocr-python` when Python is available. PaddleOCR may download models on
  first OCR use into the user profile cache.
- OCR bridge operations have per-operation timeouts and terminate the Python process tree on timeout
  or caller cancellation. OCR provisioning records progress/status in Settings and can be cancelled
  from the UI; if left running, the backend stops it after 45 minutes and reports that it can be retried.
