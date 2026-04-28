# App Certificates

Put local app code-signing certificates here when you want to sign a release build.

Supported file type:

- `*.pfx`

Do not commit private certificates or passwords. The repository `.gitignore` keeps certificate files in this directory out of version control while preserving this README and `.gitkeep`.

Recommended local name:

```text
OnlyRag-CodeSigning.pfx
```

