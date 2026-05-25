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

## Runtime Selection

Python 3.10 through 3.13 is supported. Python 3.14 is not supported by the pinned PaddlePaddle
runtime. Provisioning selects NVIDIA GPU wheels only when compatible local driver/runtime signals
are available; CPU OCR is the fallback. The app selects GPU automatically after Diagnostics proves
OCR GPU usable, unless the user saved CPU manually.

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
