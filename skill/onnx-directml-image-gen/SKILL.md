---
name: onnx-directml-image-gen
description: Technical skill for local ONNX Runtime image generation and editing in OnlyRag. Covers DirectML GPU acceleration, CPU fallback execution, LCM SDXL models, Hugging Face model metadata downloads, SHA256 integrity checks, and local canvas image editing.
---

# ONNX Runtime & DirectML Image Generation Skill

This skill provides operational and technical guidelines for managing the local image generation and editing pipeline in OnlyRag.

## 1. Official Documentation Sources

- **ONNX Runtime Documentation**: [onnxruntime.ai/docs](https://onnxruntime.ai/docs/)
- **ONNX Runtime C# API Reference**: [onnxruntime.ai/docs/api/csharp/api](https://onnxruntime.ai/docs/api/csharp/api/)
- **Microsoft DirectML Execution Provider**: [learn.microsoft.com/windows/ai/directml](https://learn.microsoft.com/en-us/windows/ai/directml/)
- **Hugging Face Hub API**: [huggingface.co/docs/hub/api](https://huggingface.co/docs/hub/api)
- **Stable Diffusion ONNX Pipeline Guidelines**: [huggingface.co/docs/diffusers/optimization/onnx](https://huggingface.co/docs/diffusers/optimization/onnx)

## 2. Model Management & Storage

- **Local Storage Path**: `%LOCALAPPDATA%\OnlyRag\models\images`
- **Default Catalog Entry**: `lcm-sdxl-olive-onnx` (`LCM SDXL Olive ONNX`), an OpenRAIL++ model entry optimized for local Windows DirectML / CPU inference.
- **Model Verification**:
  - Downloaded models undergo SHA256 hash verification against catalog manifests before being marked valid.
  - Technical placeholder or corrupted files are rejected during verification; generation is blocked until valid model files exist.
  - Download operations require explicit user consent in the desktop UI showing download size, license details, target local destination, and disk space requirement.

## 3. DirectML Execution & Fallback Architecture

- **Primary Provider**: DirectML (`Microsoft.ML.OnnxRuntime.DirectML`) for hardware acceleration across Windows-supported GPUs (NVIDIA, AMD, Intel).
- **Fallback Behavior**:
  - If DirectML initialization fails or VRAM/driver capabilities are insufficient, the runtime cleanly falls back to CPU execution (`CPUExecutionProvider`).
  - The UI displays readable status notes detailing the active provider (DirectML GPU or CPU Fallback) and any fallback diagnostics.

## 4. Image Generation & Editing Workflow

- **Generation Profiles**: Quality, Balanced, Performance, and Custom profiles configure step counts, guidance scale, and batch parameters.
- **Output Storage**: Generated images are stored under `%LOCALAPPDATA%\OnlyRag\images\generated`.
- **Canvas Editor**: Integrated local image toolbar editor allows users to:
  - Crop and rotate generated images.
  - Add text overlays and visual shapes.
  - Save edits as new local image records or option to overwrite the source file.
  - Delete unwanted images from disk and database.

## 5. Verification & Testing

Verify ONNX DirectML runtime setup and model verification using manual or automated tests:

```powershell
# Run backend solution unit tests targeting image pipeline
dotnet test .\OnlyRag.sln --configuration Release --filter "FullyQualifiedName~Image"
```

## 6. Technical Rules

1. Never send prompt text or generation queries to remote third-party cloud APIs. All image generation runs 100% locally.
2. Prompt inputs must be trimmed and sanitized without adding hidden automatic prompt modifiers.
3. Validate required ONNX model files (`text_encoder/model.onnx`, `unet/model.onnx`, `vae_decoder/model.onnx`) prior to allocating ONNX inference sessions.
