<#
.SYNOPSIS
    OPTIONAL, DESTRUCTIVE: removes the portable .exe files from every git commit,
    then tells you how to force-push and reclaim disk space.

.DESCRIPTION
    The exes were committed once (SelfishNetV2-x64/x86/arm64.exe, ~197 MB total).
    They are now untracked and .gitignore'd, but they remain inside git history.
    This script rewrites history to drop those blobs so the repository shrinks.

    Before running: the working tree must be clean and the exes must already be
    untracked (current state). The .exe files stay on disk - only git history changes.

    The script creates a safety branch `backup-before-purge`. If anything looks
    wrong after the rewrite, restore with:  git reset --hard backup-before-purge

    After the script finishes you must force-push. Reclaiming local disk space
    only happens after that, using the commands printed at the end.

.PARAMETER Force
    Skip the "type PURGE" confirmation.
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host '=============================================================='
Write-Host ' WARNING: this rewrites git history (all commits).'
Write-Host ' It requires a force-push afterwards. Do not run it if you'
Write-Host ' have other clones of this repository that you care about.'
Write-Host '=============================================================='
Write-Host ''

if ((git status --porcelain).Length -gt 0) {
    Write-Host 'Working tree is not clean. Commit or stash your changes first.'
    exit 1
}

if (-not $Force) {
    $answer = Read-Host 'Type PURGE to continue'
    if ($answer -cne 'PURGE') {
        Write-Host 'Cancelled - history was not touched.'
        return
    }
}

# Safety net: keep the current history reachable under a branch name.
git branch -f backup-before-purge HEAD | Out-Null
Write-Host 'Safety branch created: backup-before-purge'

$env:FILTER_BRANCH_SQUELCH_WARNING = '1'
$indexFilter = 'git rm --cached --ignore-unmatch SelfishNetV2-x64.exe SelfishNetV2-x86.exe SelfishNetV2-arm64.exe'

Write-Host 'Rewriting history...'
git filter-branch --force --index-filter $indexFilter --prune-empty -- --all
if ($LASTEXITCODE -ne 0) {
    Write-Host 'filter-branch failed. History is unchanged; backup-before-purge still points at the original commit.'
    exit 1
}

Write-Host ''
Write-Host 'History rewritten. Next steps (run them yourself, in order):'
Write-Host ''
Write-Host '  1) Inspect and verify:'
Write-Host '       git log --oneline --stat -5'
Write-Host '       git status'
Write-Host ''
Write-Host '  2) Force-push the rewritten history:'
Write-Host '       git push --force origin main'
Write-Host ''
Write-Host '  3) Only after the push succeeded, reclaim local space:'
Write-Host '       git update-ref -d refs/original/refs/heads/main   (repeat for each refs/original entry)'
Write-Host '       git branch -D backup-before-purge'
Write-Host '       git reflog expire --expire=now --all'
Write-Host '       git gc --prune=now'
Write-Host ''
Write-Host 'If anything went wrong before step 3:'
Write-Host '       git reset --hard backup-before-purge'
