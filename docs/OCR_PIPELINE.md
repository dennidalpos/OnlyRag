# OCR Pipeline

OnlyRag uses a Python PaddleOCR bridge for scanned PDFs and image text extraction when OCR
prerequisites are prepared.

## Components

- [`scripts/ocr/paddle_ocr_bridge.py`](../scripts/ocr/paddle_ocr_bridge.py): Python bridge called
  by the app.
- [`scripts/ocr/install_ocr_runtime.ps1`](../scripts/ocr/install_ocr_runtime.ps1): installer-time
  and setup-time OCR runtime provisioning.
- [`scripts/ocr/runtime-manifest.json`](../scripts/ocr/runtime-manifest.json): CPU/GPU runtime
  manifest.
- [`scripts/ocr/requirements-*.txt`](../scripts/ocr): pinned Python package sets.
- [`src/OnlyRag.Infrastructure/Ocr`](../src/OnlyRag.Infrastructure/Ocr): OCR engine, cache, retry,
  and settings code.
- [`src/OnlyRag.Api/OcrProvisionRuntimeResolver.cs`](../src/OnlyRag.Api/OcrProvisionRuntimeResolver.cs):
  runtime selection support used by the app.
- [`src/OnlyRag.Api/OcrRuntimeEnvironment.cs`](../src/OnlyRag.Api/OcrRuntimeEnvironment.cs):
  transactional lifecycle for the private virtual environment.

## Runtime Selection

Python 3.10 through 3.13 is supported. Python 3.14 is not supported by the pinned PaddlePaddle
runtime. Provisioning selects NVIDIA GPU wheels only when compatible local driver/runtime signals
are available; CPU OCR is the fallback. The app selects GPU automatically after Diagnostics proves
OCR GPU usable, unless the user saved CPU manually.

## Environment lifecycle and recovery

The application-owned environment is `%LOCALAPPDATA%\OnlyRag\ocr-python\.venv`. Provisioning
never changes that live directory while package installation is running: it creates a uniquely
named sibling staging directory, installs the selected pinned requirements there, and performs the
PaddleOCR bridge check there. Only a checked environment containing `Scripts\python.exe` and a
runtime stamp is published. The previous environment is retained until publication succeeds, so a
cancelled, timed-out, or failed installation leaves the last working environment untouched.

Diagnostics classify the local environment as `missing`, `incomplete`, `corrupt`, or `ready`
without exposing Python command output or local paths to the UI. The dependency status endpoint
keeps progress, the selected runtime, a safe user-facing error, and a retry action. Use **Install
OCR** (or **Repair OCR**) to rebuild an incomplete or corrupt runtime; the action may be cancelled
from Diagnostics and has a 45-minute application timeout.

## Verification

Manifest validation:

```powershell
pwsh .\scripts\ocr\Test-OcrRuntimeManifest.ps1
```

Catalog drift check:

```powershell
pwsh .\scripts\ocr\Test-OcrRuntimeCatalog.ps1 -OutputPath .\artifacts\ocr-runtime-catalog\ocr-runtime-catalog.json
```

The scheduled GitHub workflow [`ocr-runtime-catalog.yml`](../.github/workflows/ocr-runtime-catalog.yml)
runs the catalog check for Python 3.10 through 3.13 and opens a maintenance issue when a reachable
PaddlePaddle GPU runtime is missing from the manifest.

## Limits

- OCR is optional; missing Python or package preparation must not block non-OCR workflows.
- Requirements are pinned but not hash-locked.
- Internet access is required when provisioning downloads Python packages.
