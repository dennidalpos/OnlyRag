# Image Generation

OnlyRag uses a single integrated local image provider. Users do not install or start separate image
tools, and prompts are not sent to remote endpoints.

## Models

The Images section lists curated models with:

- model id and display name;
- recommended GPU/CPU profile;
- download URL;
- license label;
- expected size;
- required local files;
- SHA256 verification metadata.

Model files are stored under:

```powershell
%LOCALAPPDATA%\OnlyRag\models\images
```

Downloads are never bundled into the installer and are not repository-tracked source files. Before
download starts, the UI asks for explicit consent and shows size, license, local destination, and
disk-space impact.

## Runtime

OnlyRag prefers DirectML GPU execution on Windows when available and falls back to CPU. The Images UI
shows the active execution provider and blocks generation until the selected model is downloaded and
verified.

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
