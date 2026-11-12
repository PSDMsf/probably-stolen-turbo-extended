param(
    [string]$GameDir = $env:PROBABLY_STOLEN_DIR,
    [string]$Configuration = 'Release',
    [string]$PreviewPath,
    [string]$DescriptionPath,
    [string]$PublishId = '0',
    [string]$Visibility = '2',
    [switch]$SkipBuild,
    [switch]$WriteVdf
)
# NOTE: keep this file ASCII-only (Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI).
$ErrorActionPreference = 'Stop'

$repo      = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repo 'src\ras_turbo_boost.csproj'
$manifest  = Join-Path $repo 'src\manifest.xml'
$contentDir = Join-Path $PSScriptRoot 'content'
$dllName   = 'ras_turbo_boost.dll'

if (-not $PreviewPath)      { $PreviewPath = Join-Path $PSScriptRoot 'preview.png' }
if (-not $DescriptionPath)  { $DescriptionPath = Join-Path $repo 'docs\workshop-description.zh.md' }

# ---- 1. build -------------------------------------------------------------
if (-not $SkipBuild) {
    $args = @('build', $project, '-c', $Configuration, '-v', 'minimal', '-p:DeployToMods=false')
    if ($GameDir) { $args += "-p:GameDir=$GameDir" }
    Write-Host "> dotnet $($args -join ' ')" -ForegroundColor Cyan
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}

$dll = Join-Path $repo "src\bin\$Configuration\netstandard2.1\$dllName"
if (-not (Test-Path $dll)) { throw "DLL not found: $dll (build first)" }
if (-not (Test-Path $manifest)) { throw "manifest not found: $manifest" }
if (-not (Test-Path $PreviewPath)) { throw "preview image not found: $PreviewPath (128x128+ png, < 1 MB)" }

# ---- 2. version -----------------------------------------------------------
[xml]$mf = [System.IO.File]::ReadAllText($manifest, [System.Text.Encoding]::UTF8)
$version = $mf.Manifest.ModVersion
$modId   = $mf.Manifest.ID

# ---- 3. workshop content --------------------------------------------------
if (Test-Path $contentDir) { Remove-Item $contentDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $contentDir | Out-Null
Copy-Item $manifest (Join-Path $contentDir 'manifest.xml') -Force
Copy-Item $dll      (Join-Path $contentDir $dllName) -Force
Copy-Item $PreviewPath (Join-Path $contentDir 'preview.png') -Force

$previewSize = (Get-Item $PreviewPath).Length
if ($previewSize -gt 1MB) { Write-Warning "preview.png is $([math]::Round($previewSize/1KB)) KB; Steam prefers < 1 MB" }

# ---- 4. release zip -------------------------------------------------------
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$stage = Join-Path $dist $modId
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $manifest (Join-Path $stage 'manifest.xml') -Force
Copy-Item $dll      (Join-Path $stage $dllName) -Force
$zip = Join-Path $dist "$modId-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# ---- 5. vdf ---------------------------------------------------------------
if ($WriteVdf) {
    $vdf = Join-Path $PSScriptRoot 'workshop.vdf'
    $desc = [System.IO.File]::ReadAllText($DescriptionPath, [System.Text.Encoding]::UTF8)
    # VDF: escape double quotes; real newlines inside a quoted value are fine
    $esc = $desc.Replace('"', "'")
    # Title comes from manifest.xml so this script stays ASCII-only
    $title = $mf.Manifest.Name + ' / Better Turbo Booster'
    $template = @"
"workshopitem"
{
    "appid"            "4348910"
    "publishedfileid"  "$PublishId"
    "contentfolder"    "$($contentDir -replace '\\','\\')"
    "previewfile"      "$($PreviewPath -replace '\\','\\')"
    "visibility"       "$Visibility"
    "title"            "$title"
    "description"      "$esc"
    "changenote"       "$version"
}
"@
    [System.IO.File]::WriteAllText($vdf, $template, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "Wrote $vdf" -ForegroundColor Green
}

Write-Host ""
Write-Host "version : $version"
Write-Host "mod id  : $modId"
Write-Host "content : $contentDir"
Write-Host "zip     : $zip"
Write-Host ""
Write-Host "next:" -ForegroundColor Green
Write-Host "  steamcmd +login <account> +workshop_build_item `"$(Join-Path $PSScriptRoot 'workshop.vdf')`" +quit"
Write-Host "  (visibility 2 = hidden for testing, 0 = public)"
