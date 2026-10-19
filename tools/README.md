# tools/ — project maintenance scripts

| Script | Purpose |
|---|---|
| `clean.cmd` / `clean.ps1` | Deletes regenerable build artifacts (`src/**/bin|obj`, `publish/`, the prototype's stale exe). Keeps the root portable exes unless `-IncludeExes` is passed. Safe by construction: refuses to delete anything outside the project. |
| `purge-binaries-from-history.ps1` | **Optional and destructive.** Rewrites git history to remove the ~197 MB of portable exes that were committed once, then prints the force-push and disk-reclaim steps. Keeps the exes on disk; creates a `backup-before-purge` branch. |

## Usage

```bat
tools\clean.cmd                 :: prompts, then removes build artifacts
tools\clean.cmd -Force          :: no prompt
tools\clean.cmd -IncludeExes    :: also deletes SelfishNetV2-*.exe from the root
```

```powershell
powershell -ExecutionPolicy Bypass -File tools\purge-binaries-from-history.ps1
```

No Administrator rights are needed for `clean.cmd` — it only touches files inside the
project folder. See the script headers for details.

## Artifact policy

- **Source, docs and reference material are versioned.**
- **Build output is never versioned** (`bin/`, `obj/`, `publish/` are ignored).
- **Portable exes are deliverables, not source**: they live in the project root for
  convenience and are ignored by git. If a build ever needs to be distributed, attach it
  to a GitHub Release (or use Git LFS deliberately) instead of committing it — this keeps
  clones small and every future commit light.
