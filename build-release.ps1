# Gausslite — release build script
#
# Pipeline:
#   1. Read version from Directory.Build.props (single source of truth).
#   2. dotnet publish (self-contained, multi-file, x64).
#      Note: PublishSingleFile is incompatible with EnableCoreMrtTooling=false
#      (which the project sets to avoid VS2022 AppxPackage tooling that the
#      dotnet CLI SDK doesn't ship with). Multi-file gives a ~200 MB folder
#      that compresses to ~70 MB inside Inno's LZMA2 archive.
#   3. Compile installer/Gausslite.iss via ISCC.exe -> GaussliteSetup-{ver}.exe
#   4. Pack publish output as Gausslite-{ver}-portable.zip.
#   5. Print SHA-256 hashes of both artifacts.
#
# Prerequisite: Inno Setup 6 installed (https://jrsoftware.org/isdl.php).
#
# Outputs land in installer\Output\.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ------------------------------------------------------------ paths + version
$RepoRoot       = $PSScriptRoot
$AppCsproj      = Join-Path $RepoRoot 'src\Gausslite.App\Gausslite.App.csproj'
$PropsPath      = Join-Path $RepoRoot 'Directory.Build.props'
$PublishDir     = Join-Path $RepoRoot 'publish'
$InstallerDir   = Join-Path $RepoRoot 'installer'
$IssScript      = Join-Path $InstallerDir 'Gausslite.iss'
$OutputDir      = Join-Path $InstallerDir 'Output'

$propsXml = [xml](Get-Content $PropsPath)
$Version  = ($propsXml.Project.PropertyGroup.Version | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Could not extract <Version> from Directory.Build.props."
}
Write-Host "Building release artifacts for Gausslite v$Version" -ForegroundColor Cyan

# ------------------------------------------------------------ ISCC discovery
function Find-ISCC {
    $candidates = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($k in $candidates) {
        if (Test-Path $k) {
            $instLoc = (Get-ItemProperty $k -ErrorAction SilentlyContinue).InstallLocation
            if ($instLoc) {
                $candidate = Join-Path $instLoc 'ISCC.exe'
                if (Test-Path $candidate) { return $candidate }
            }
        }
    }
    # Fallback to PATH lookup.
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php'
}

$ISCC = Find-ISCC
Write-Host "Using Inno compiler at: $ISCC"

# ------------------------------------------------------------ clean
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
# Don't fail if Output dir is locked (File Explorer browsing into it). Clean
# the contents we know about; new artifacts overwrite by name.
if (Test-Path $OutputDir) {
    Get-ChildItem $OutputDir -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# ------------------------------------------------------------ publish
Write-Host "`n[1/3] dotnet publish (self-contained, win-x64)..." -ForegroundColor Yellow
& dotnet publish $AppCsproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:SatelliteResourceLanguages=en `
    --output $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# ------------------------------------------------------------ Inno
Write-Host "`n[2/3] Compiling installer with Inno Setup..." -ForegroundColor Yellow
& $ISCC $IssScript
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$InstallerExe = Join-Path $OutputDir "GaussliteSetup-$Version.exe"
if (-not (Test-Path $InstallerExe)) {
    throw "Expected installer not found: $InstallerExe"
}

# ------------------------------------------------------------ portable zip
Write-Host "`n[3/3] Packing portable zip..." -ForegroundColor Yellow
$PortableZip = Join-Path $OutputDir "Gausslite-$Version-portable.zip"
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $PortableZip -Force

# ------------------------------------------------------------ hashes + summary
function Get-Sha256 {
    param([string]$Path)
    (Get-FileHash -Algorithm SHA256 -Path $Path).Hash
}

$installerSize = '{0:N2} MB' -f ((Get-Item $InstallerExe).Length / 1MB)
$portableSize  = '{0:N2} MB' -f ((Get-Item $PortableZip).Length / 1MB)

Write-Host "`n========== Release artifacts ==========" -ForegroundColor Green
Write-Host ("  {0}  ({1})" -f $InstallerExe, $installerSize)
Write-Host ("    SHA-256: {0}" -f (Get-Sha256 $InstallerExe))
Write-Host ("  {0}  ({1})" -f $PortableZip, $portableSize)
Write-Host ("    SHA-256: {0}" -f (Get-Sha256 $PortableZip))
Write-Host "=======================================" -ForegroundColor Green
