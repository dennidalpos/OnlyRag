# OCR Pipeline (Dual-Engine Architecture)

OnlyRag features a dual-engine OCR architecture:
1. **Native C# DirectML ONNX OCR Engine** ([`OnnxDirectMlOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/OnnxDirectMlOcrEngine.cs)): Built-in in-process OCR engine running directly via ONNX Runtime DirectML with hardware GPU acceleration. Requires zero external Python runtime dependencies.
2. **Python PaddleOCR Bridge** ([`PaddleOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/PaddleOcrEngine.cs)): Subprocess bridge for PaddleOCR when Python OCR prerequisites (Python 3.10-3.13) are installed.

## Components

- [`src/OnlyRag.Infrastructure/Ocr/OnnxDirectMlOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/OnnxDirectMlOcrEngine.cs): Native C# DirectML ONNX OCR engine implementation.
- [`src/OnlyRag.Infrastructure/Ocr/PaddleOcrEngine.cs`](../src/OnlyRag.Infrastructure/Ocr/PaddleOcrEngine.cs): Python PaddleOCR subprocess bridge.
- [`scripts/ocr/paddle_ocr_bridge.py`](../scripts/ocr/paddle_ocr_bridge.py): Python bridge script.
- [`scripts/ocr/install_ocr_runtime.ps1`](../scripts/ocr/install_ocr_runtime.ps1): Python OCR runtime provisioning script.
- [`scripts/ocr/runtime-manifest.json`](../scripts/ocr/runtime-manifest.json): CPU/GPU runtime manifest for PaddleOCR.
- [`src/OnlyRag.Infrastructure/Ocr`](../src/OnlyRag.Infrastructure/Ocr): OCR engine factory (`IOcrEngine`), SQLite cache (`SqliteOcrCacheRepository`), retry policies, and settings store.
- [`src/OnlyRag.Api/OcrProvisionRuntimeResolver.cs`](../src/OnlyRag.Api/OcrProvisionRuntimeResolver.cs): runtime selection resolver.
- [`src/OnlyRag.Api/OcrRuntimeEnvironment.cs`](../src/OnlyRag.Api/OcrRuntimeEnvironment.cs): transactional virtual environment lifecycle.

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
without exposing Python command output or local paths to the UI. For an `incomplete` or `corrupt`
environment, the dependency status endpoint starts one background repair attempt per application
session. Its progress, selected runtime, timeout and cancellation state are observable in the UI.
The attempt uses the same 45-minute timeout and transactional staging as manual provisioning. If
it is cancelled, times out, or fails, automatic retries stop for that session and the UI exposes
**Repair OCR** as the explicit fallback. Missing environments still require **Install OCR**.

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
