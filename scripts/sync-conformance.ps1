<#
.SYNOPSIS
    Copies the conformance tests of the FormuLang standard into tests/FormuLang.NET.Conformance.Tests/conformance.

.DESCRIPTION
    The standard repository (https://github.com/Adisoreq/FormuLang) holds language-independent test cases in its
    tests/ directory. This script replaces the local copy with the files of a checkout of that repository and records
    its revision in conformance/REVISION. Run it every time the library is synced with a newer revision of the
    standard (docs/development.md).

.PARAMETER Standard
    Path of a checkout of the standard repository. Default: ../FormuLang next to this repository.

.EXAMPLE
    pwsh scripts/sync-conformance.ps1
    pwsh scripts/sync-conformance.ps1 -Standard D:\src\FormuLang
#>
param(
    [string] $Standard = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'FormuLang')
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $Standard 'tests'
$target = [IO.Path]::Combine((Split-Path $PSScriptRoot -Parent), 'tests', 'FormuLang.NET.Conformance.Tests', 'conformance')

if (-not (Test-Path (Join-Path $source 'schema.json'))) {
    throw "No conformance tests found in '$source'. Pass the path of the standard repository with -Standard."
}

$revision = (git -C $Standard rev-parse --short HEAD).Trim()
$dirty = git -C $Standard status --porcelain -- tests
if ($dirty) { $revision += ' (with uncommitted changes)' }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Path $target | Out-Null

Copy-Item (Join-Path $source 'schema.json') $target
Get-ChildItem $source -Directory | ForEach-Object { Copy-Item $_.FullName $target -Recurse }

@"
Adisoreq/FormuLang@$revision
Copied from https://github.com/Adisoreq/FormuLang/tree/$($revision.Split(' ')[0])/tests (CC BY 4.0) by scripts/sync-conformance.ps1.
Do not edit these files here: change the standard, then run the script again.
"@ | Set-Content (Join-Path $target 'REVISION') -NoNewline

$count = (Get-ChildItem $target -Recurse -Filter '*.json' | Where-Object Name -ne 'schema.json').Count
Write-Host "Copied $count test files from Adisoreq/FormuLang@$revision."
