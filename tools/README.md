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

- **Source, docs and reference material are versioned.**
- **Build output is never versioned** (`bin/`, `obj/`, `publish/` are ignored).
- **Portable exes are deliverables, not source.** They live in the project root for convenience and
  are ignored by git. To share a build, attach the exe to a GitHub Release instead of committing it
  — this keeps clones small and every commit light.
