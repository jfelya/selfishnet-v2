<#
.SYNOPSIS
    Removes regenerable build artifacts from the SelfishNet v2 project.

.DESCRIPTION
    Deletes only what can be rebuilt from source:
      - every bin/ and obj/ folder under src/
      - the publish/ output folder

    The local portable executables in the project root (SelfishNetV2-*.exe)
    are git-ignored build artifacts and are KEPT unless -IncludeExes is passed.

    The script refuses to delete anything outside the project folder.

.PARAMETER IncludeExes
    Also delete the root portable executables. Rebuild them with src\publish.bat.

.PARAMETER Force
    Skip the confirmation prompt (for unattended use).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\clean.ps1
    powershell -ExecutionPolicy Bypass -File tools\clean.ps1 -IncludeExes -Force
#>
[CmdletBinding()]
param(
    [switch]$IncludeExes,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\')

function Get-Size([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return 0 }
    $sum = (Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [int64]$sum
}

function Remove-Safe([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete outside the project: $full"
    }
    if (-not (Test-Path -LiteralPath $full)) { return [int64]0 }
    $size = Get-Size $full
    Remove-Item -LiteralPath $full -Recurse -Force
    return $size
}

# ---- collect targets (all absolute, all inside the project) ----
$targets = New-Object System.Collections.Generic.List[string]

Get-ChildItem -LiteralPath (Join-Path $root 'src') -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    $targets.Add((Join-Path $_.FullName 'bin'))
    $targets.Add((Join-Path $_.FullName 'obj'))
}

$targets.Add((Join-Path $root 'publish'))

if ($IncludeExes) {
    Get-ChildItem -LiteralPath $root -Filter 'SelfishNetV2-*.exe' -File -ErrorAction SilentlyContinue |
        ForEach-Object { $targets.Add($_.FullName) }
}

$existing = $targets | Where-Object { Test-Path -LiteralPath $_ }

if ($existing.Count -eq 0) {
    Write-Host 'Nothing to clean - the project folder is already tidy.'
    return
}

Write-Host 'SelfishNet v2 - cleanup targets:'
$total = [int64]0
foreach ($t in $existing) {
    $size = Get-Size $t
    $total += $size
    Write-Host ("  {0,8:N1} MB  {1}" -f ($size / 1MB), $t.Replace($rootFull + '\', ''))
}
Write-Host ("  {0,8:N1} MB  TOTAL" -f ($total / 1MB))
Write-Host ''

if (-not $Force) {
    $answer = Read-Host 'Delete these items? [y/N]'
    if ($answer -notin @('y', 'Y', 'yes', 'YES')) {
        Write-Host 'Cancelled - nothing was deleted.'
        return
    }
}

$freed = [int64]0
foreach ($t in $existing) { $freed += (Remove-Safe $t) }

Write-Host ''
Write-Host ("Done. Reclaimed {0:N1} MB." -f ($freed / 1MB))
Write-Host 'Rebuild with: dotnet build src\SelfishNetV2.sln -c Release'
Write-Host 'Portable exes:  src\publish.bat'
