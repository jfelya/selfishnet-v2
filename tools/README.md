# tools/ — project maintenance

| Script | Purpose |
|---|---|
| `clean.cmd` / `clean.ps1` | Deletes regenerable build artifacts (`src/**/bin|obj`, `publish/`). Keeps the root portable exes unless `-IncludeExes` is passed. Safe by construction: it refuses to delete anything outside the project. |

## Usage

```bat
tools\clean.cmd                 :: prompts, then removes build artifacts
tools\clean.cmd -Force          :: no prompt
tools\clean.cmd -IncludeExes    :: also deletes SelfishNetV2-*.exe from the root
```

No Administrator rights are needed — it only touches files inside the project folder.

## Artifact policy

- **Source, docs and guides are versioned.** The two portable exes at the root and all build
  output are **git-ignored**; build them with `src\publish.bat` when needed.
- A fresh clone contains source and docs, not binaries. This keeps the repository small and
  avoids Git file-size limits.
- `tools\clean.cmd -IncludeExes` also deletes the local root copies.
