# Generates placeholder PNG assets required by Package.appxmanifest.
# Run automatically by the EnsureAssets MSBuild target on first build.
# Re-run manually any time the Assets folder is missing.

Add-Type -AssemblyName System.Drawing

function New-Asset {
    param([string]$Path, [int]$Width, [int]$Height,
          [int]$R = 0, [int]$G = 120, [int]$B = 212)

    $dir = Split-Path $Path -Parent
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

    $bmp = [System.Drawing.Bitmap]::new($Width, $Height)
    $gr  = [System.Drawing.Graphics]::FromImage($bmp)
    $gr.Clear([System.Drawing.Color]::FromArgb(255, $R, $G, $B))
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gr.Dispose(); $bmp.Dispose()
    Write-Host "  $Path ($Width x $Height)"
}

Write-Host "Generating assets..."
New-Asset "Assets\Square44x44Logo.png"   44  44
New-Asset "Assets\Square150x150Logo.png" 150 150
New-Asset "Assets\Wide310x150Logo.png"   310 150
New-Asset "Assets\StoreLogo.png"          50  50
New-Asset "Assets\SplashScreen.png"      620 300  240 240 240
Write-Host "Done."
