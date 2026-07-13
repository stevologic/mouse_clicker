<#
    build.ps1 — compiles mouseclicker.app into a single portable .exe using the
    .NET Framework C# compiler that ships with Windows. No SDK, no NuGet,
    no runtime download. Output: MouseClicker.exe (runs on any Win10/11).

    Usage:   powershell -ExecutionPolicy Bypass -File build.ps1
             powershell -ExecutionPolicy Bypass -File build.ps1 -Run
             powershell -ExecutionPolicy Bypass -File build.ps1 -Sign

    Code signing (removes the Windows "unknown publisher" / SmartScreen prompt):
    the build Authenticode-signs the exe when a certificate is provided via
    environment variables — no Windows SDK / signtool needed. Set ONE of:
      $env:CODESIGN_PFX            path to a .pfx code-signing certificate
      $env:CODESIGN_PFX_PASSWORD   password for that .pfx (if any)
    -- or --
      $env:CODESIGN_THUMBPRINT     thumbprint of a cert already in your store
    Optional:
      $env:CODESIGN_TIMESTAMP_URL  RFC3161 timestamp URL
                                   (default http://timestamp.digicert.com)
    Pass -Sign to fail the build if no certificate is configured. Without a
    certificate the build still succeeds, unsigned. See README "Is it safe?".
#>
param(
    [switch]$Run,
    [switch]$Sign
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

# --- Code signing (optional) ------------------------------------------------
# Signs the exe so Windows shows the real publisher instead of the scary
# "unknown publisher" warning. Uses the built-in Set-AuthenticodeSignature
# cmdlet, so no Windows SDK / signtool is required.
function Get-CodeSignCert {
    if ($env:CODESIGN_PFX -and (Test-Path $env:CODESIGN_PFX)) {
        try {
            if ($env:CODESIGN_PFX_PASSWORD) {
                $sec = ConvertTo-SecureString $env:CODESIGN_PFX_PASSWORD -AsPlainText -Force
                return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:CODESIGN_PFX, $sec)
            }
            return Get-PfxCertificate -FilePath $env:CODESIGN_PFX
        } catch {
            Write-Warning "Could not load PFX at CODESIGN_PFX: $_"
            return $null
        }
    }
    if ($env:CODESIGN_THUMBPRINT) {
        $tp = ($env:CODESIGN_THUMBPRINT -replace '[^0-9A-Fa-f]', '').ToUpper()
        foreach ($store in 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\My') {
            try {
                $c = Get-ChildItem $store -CodeSigningCert -ErrorAction SilentlyContinue |
                     Where-Object { $_.Thumbprint -eq $tp } | Select-Object -First 1
                if ($c) { return $c }
            } catch {}
        }
        Write-Warning "No code-signing cert with thumbprint $tp in CurrentUser/LocalMachine 'My' store."
    }
    return $null
}

$cert = Get-CodeSignCert
if ($cert) {
    $ts = if ($env:CODESIGN_TIMESTAMP_URL) { $env:CODESIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    Write-Host "Signing: $($cert.Subject)" -ForegroundColor DarkGray
    $sig = Set-AuthenticodeSignature -FilePath $out -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer $ts -ErrorAction SilentlyContinue
    if ($sig -and $sig.SignerCertificate) {
        Write-Host "Signed OK  [status: $($sig.Status)]" -ForegroundColor Green
        if ($sig.Status -ne 'Valid') {
            Write-Host "  Note: status '$($sig.Status)' just means this build machine doesn't fully trust the chain; end users whose Windows trusts the CA root will see a valid signature." -ForegroundColor DarkGray
        }
    } else {
        Write-Error "Signing FAILED: $($sig.Status) $($sig.StatusMessage)"
        exit 1
    }
} elseif ($Sign) {
    Write-Error "-Sign requested but no signing certificate configured (set CODESIGN_PFX or CODESIGN_THUMBPRINT)."
    exit 1
} else {
    Write-Host "Unsigned build (no signing certificate configured). To remove the Windows 'unknown publisher' prompt, see README 'Is it safe?'." -ForegroundColor DarkGray
}

if ($Run) {
    Write-Host "Launching..." -ForegroundColor DarkGray
    Start-Process $out
}
