param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$images = @(
    @{ Name = 'cla-sample.png'; Color = [System.Drawing.Color]::CornflowerBlue },
    @{ Name = 'obd-sample.png'; Color = [System.Drawing.Color]::ForestGreen },
    @{ Name = 'seg-sample.png'; Color = [System.Drawing.Color]::DarkOrange }
)

foreach ($image in $images) {
    $path = Join-Path $OutputDirectory $image.Name
    $bitmap = New-Object System.Drawing.Bitmap 64, 64
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear($image.Color)
            $graphics.FillEllipse([System.Drawing.Brushes]::White, 16, 16, 32, 32)
        }
        finally { $graphics.Dispose() }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

Write-Output "Created anonymous acceptance images in $OutputDirectory"
