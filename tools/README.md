# tools/ — project maintenance

| Script | Purpose |
|---|---|
| `clean.cmd` / `clean.ps1` | Deletes regenerable build artifacts (`src/**/bin|obj`, `publish/`, the prototype's stale exe). Keeps the root portable exes unless `-IncludeExes` is passed. Safe by construction: it refuses to delete anything outside the project. |

## Usage

```bat
tools\clean.cmd                 :: prompts, then removes build artifacts
tools\clean.cmd -Force          :: no prompt
tools\clean.cmd -IncludeExes    :: also deletes SelfishNetV2-*.exe from the root
```

No Administrator rights are needed — it only touches files inside the project folder.

## Artifact policy

- **Source, docs, reference material and the two portable exes at the root are versioned**, so a
  fresh clone is ready to run.
- **Build output is never versioned** (`bin/`, `obj/`, `publish/` are ignored).
- Rebuilding the exes means committing new binaries (~130 MB per version). That is a deliberate
  trade-off for clone-and-run convenience.
