<#
.SYNOPSIS
    Packs the library for a release: the NuGet packages (.nupkg) and their symbol packages (.snupkg).

.DESCRIPTION
    Does locally what the Release workflow (.github/workflows/release.yml) does before it publishes: builds the
    solution in Release, runs the tests, and packs FormuLang.NET.Abstractions, FormuLang.NET.Core and
    FormuLang.NET.Extensions.Stock into the output directory. The version comes from <Version> in
    Directory.Build.props. Nothing is published: push the packages with `dotnet nuget push`, or push a version tag
    and let the Release workflow do it (docs/development.md, "Releasing").

.PARAMETER Output
    Directory of the packages. Default: artifacts in the repository (ignored by git). Old packages are removed first.

.PARAMETER SkipTests
    Packs without running the tests.

.EXAMPLE
    pwsh scripts/pack.ps1
    pwsh scripts/pack.ps1 -SkipTests -Output D:\packages
#>
param(
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts'),
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$solution = Join-Path $root 'FormuLang.NET.sln'
$projects = 'FormuLang.NET.Abstractions', 'FormuLang.NET.Core', 'FormuLang.NET.Extensions.Stock'

function Invoke-Step([string] $title, [scriptblock] $command) {
    Write-Host "`n==> $title" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)." }
}

# Version of the packages: <Version> in Directory.Build.props
[xml] $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> found in Directory.Build.props.' }
Write-Host "FormuLang.NET $version" -ForegroundColor Green

$released = git -C $root tag --list "v$version" "$version"
if ($released) { Write-Warning "Version $version is already tagged ($($released -join ', ')): set a new <Version> in Directory.Build.props before a release." }

$dirty = git -C $root status --porcelain
if ($dirty) { Write-Warning 'The working tree has uncommitted changes: SourceLink will point to a commit without them.' }

Invoke-Step 'Build (Release)' { dotnet build $solution -c Release --nologo }

if (-not $SkipTests) {
    Invoke-Step 'Test' { dotnet test $solution -c Release --no-build --nologo }
}

if (Test-Path $Output) {
    Get-ChildItem $Output -Include '*.nupkg', '*.snupkg' -Recurse | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Path $Output | Out-Null
}
$Output = Resolve-Path $Output

foreach ($project in $projects) {
    Invoke-Step "Pack $project" { dotnet pack (Join-Path $root 'src' $project) -c Release --no-build --nologo -o $Output }
}

Write-Host "`nPackages in ${Output}:" -ForegroundColor Green
Get-ChildItem $Output -Include '*.nupkg', '*.snupkg' -Recurse |
    Sort-Object Name |
    ForEach-Object { '  {0,-55} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }

$missing = $projects | Where-Object { -not (Test-Path (Join-Path $Output "$_.$version.nupkg")) }
if ($missing) { throw "Missing packages of version ${version}: $($missing -join ', ')" }
