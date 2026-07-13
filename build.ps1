<#
    build.ps1 — compiles mouseclicker.app into a single portable .exe using the
    .NET Framework C# compiler that ships with Windows. No SDK, no NuGet,
    no runtime download. Output: MouseClicker.exe (runs on any Win10/11).

    Usage:   powershell -ExecutionPolicy Bypass -File build.ps1
             powershell -ExecutionPolicy Bypass -File build.ps1 -Run
#>
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

# --- Locate the Framework C# compiler ---------------------------------------
$csc = $null
$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $csc = $c; break }
}
if (-not $csc) {
    Write-Error "Could not find csc.exe. The .NET Framework 4.x C# compiler is required (ships with Windows)."
    exit 1
}
Write-Host "Using compiler: $csc" -ForegroundColor DarkGray

# --- Generate an application icon if missing --------------------------------
$assets = Join-Path $root 'assets'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }
$iconPath = Join-Path $assets 'icon.ico'

if (-not (Test-Path $iconPath)) {
    Write-Host "Generating icon..." -ForegroundColor DarkGray
    Add-Type -AssemblyName System.Drawing
    $size = 64
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [int]($size * 0.16)
    $bx = $pad + [int]($size * 0.06)
    $bw = $size - 2*$pad - [int]($size * 0.12)
    $bh = $size - 2*$pad
    $rect = New-Object System.Drawing.Rectangle $bx, $pad, $bw, $bh

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($bx, $pad, $bw, $bw, 180, 180)
    $path.AddArc($bx, ($pad + $bh - $bw), $bw, $bw, 0, 180)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(99,130,255)
    $c2 = [System.Drawing.Color]::FromArgb(150,110,255)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, 60
    $g.FillPath($brush, $path)

    $wheel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $ww = [int]($bw * 0.16)
    $g.FillRectangle($wheel, ($bx + $bw/2 - $ww/2), ($pad + $bh*0.16), $ww, ($bh*0.22))

    $g.Dispose()
    $hicon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hicon)
    $fs = New-Object System.IO.FileStream $iconPath, ([System.IO.FileMode]::Create)
    $icon.Save($fs)
    $fs.Close()
    $bmp.Dispose()
}

# --- Compile ----------------------------------------------------------------
$src = Get-ChildItem -Path (Join-Path $root 'src') -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if (-not $src) { Write-Error "No source files found in src\"; exit 1 }

$out = Join-Path $root 'MouseClicker.exe'
$manifest = Join-Path $assets 'app.manifest'

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll',
    'System.Net.Http.dll'
)

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/langversion:5',
    "/out:$out",
    "/win32manifest:$manifest"
)
if (Test-Path $iconPath) { $args += "/win32icon:$iconPath" }
foreach ($r in $refs) { $args += "/reference:$r" }
$args += $src

Write-Host "Compiling $($src.Count) source files..." -ForegroundColor DarkGray
& $csc @args
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (csc exit $LASTEXITCODE)."
    exit $LASTEXITCODE
}

$sizeKb = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "Build succeeded -> MouseClicker.exe ($sizeKb KB)" -ForegroundColor Green

if ($Run) {
    Write-Host "Launching..." -ForegroundColor DarkGray
    Start-Process $out
}
