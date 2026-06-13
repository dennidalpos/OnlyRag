# Image Generation

OnlyRag uses a single integrated local image provider. Users do not install or start separate image
tools, and prompts are not sent to remote endpoints.

## Models

The Images section lists curated models with:

- model id and display name;
- recommended GPU/CPU profile;
- editable download URL or Hugging Face repository URL;
- license label;
- expected size;
- required local files;
- SHA256 verification metadata.

The built-in catalog is seeded with DirectML-compatible SDXL/LCM ONNX entries. Users can edit
built-in entries, reset them, or add manual entries from the Images section. Catalog overrides are
stored in the local settings store, not in repository source files.

When a catalog entry points at `https://huggingface.co/{owner}/{model}`, OnlyRag downloads the model
repository snapshot into the local model folder. Direct file URLs are downloaded as `model.onnx`.

Model files are stored under:

```powershell
%LOCALAPPDATA%\OnlyRag\models\images
```

Downloads are never bundled into the installer and are not repository-tracked source files. Before
download starts, the UI asks for explicit consent and shows size, license, local destination, and
disk-space impact.

## Runtime

OnlyRag uses the integrated ONNX Stable Diffusion/SDXL pipeline. On Windows it prefers DirectML GPU
execution, including on NVIDIA GPUs, and falls back to CPU if the DirectML provider cannot be
initialized for the selected model or device. The runtime status records the preferred provider, the
active provider, and the readable fallback reason shown by the UI.

The Images UI blocks generation until the selected model is downloaded and verified. Technical
placeholder files are rejected during verification and generation is blocked instead of producing fake
pattern images.

Generated images are stored under:

```powershell
%LOCALAPPDATA%\OnlyRag\images\generated
```

Users can delete generated images from the editor and can open the generated-images folder through the
Images section after explicit confirmation.

## Release Verification

Representative release verification should cover:

- model catalog visibility;
- explicit download consent;
- SHA256 verification failure and success;
- GPU execution when DirectML is available;
- CPU fallback when GPU acceleration is unavailable;
- prompt generation;
- saved gallery entries;
- generated file retrieval.
