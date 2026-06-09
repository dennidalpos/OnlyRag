# Image Generation

OnlyRag generates images through external local providers. The app does not bundle or install image
models, Stable Diffusion runtimes, Python environments, CUDA packages, Automatic1111, or ComfyUI.

## Supported Providers

- Automatic1111 at `http://127.0.0.1:7860`, with the API enabled by starting WebUI with `--api`.
- ComfyUI at `http://127.0.0.1:8188`, verified through the `system_stats` endpoint.

Both providers are optional. OnlyRag can run without either provider; the Images section reports
provider status and lets users configure different trusted endpoints when needed.

## Setup Checks

`scripts\Bootstrap-Prerequisites.ps1` checks both default local endpoints:

```powershell
pwsh .\scripts\Bootstrap-Prerequisites.ps1
```

The checks are non-blocking:

- Reachable endpoints are reported as verified.
- Missing or unreachable endpoints are reported as warnings with manual setup guidance.
- Use `-SkipImageGenerationCheck` to skip these checks intentionally.

## Manual Provider Setup

Automatic1111:

1. Install Automatic1111 from the official repository.
2. Start it with API support, usually by adding `--api` to the startup arguments.
3. Confirm `http://127.0.0.1:7860/sdapi/v1/sd-models` responds.
4. Open OnlyRag Images and select Automatic1111.

ComfyUI:

1. Install ComfyUI using the official Windows portable or manual path.
2. Start ComfyUI locally.
3. Confirm `http://127.0.0.1:8188/system_stats` responds.
4. Open OnlyRag Images and select ComfyUI.

OnlyRag stores generated image files under `%LOCALAPPDATA%\OnlyRag` with the rest of local app data.

## Release Verification

`scripts\Test-InstallerRelease.ps1` records two optional checks:

- `optional-image-automatic1111`
- `optional-image-comfyui`

They pass when the local endpoint is reachable and warn when it is not. Warnings are expected on
machines that do not have an image generation provider installed.
