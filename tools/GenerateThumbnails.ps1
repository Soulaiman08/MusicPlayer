param([int]$Width = 200)
Add-Type -AssemblyName System.Drawing
$src = "C:\Users\win\Desktop\Soulaiman\src\MusicPlayer\Wallpaper\Live"
$dst = "C:\Users\win\Desktop\Soulaiman\src\MusicPlayer\Wallpaper\Thumbnails\Live"
New-Item -ItemType Directory -Path $dst -Force | Out-Null
$files = Get-ChildItem $src -File | Where-Object { $_.Extension -match '\.(jpg|jpeg|png|webp|bmp)$' }
$count = 0
$targetRatio = 16.0 / 9.0
$targetH = [int]($Width / $targetRatio)
foreach ($f in $files) {
    $outPath = Join-Path $dst ($f.Name)
    if (Test-Path $outPath) {
        $srcMeta = Get-Item $outPath
        if ($srcMeta.LastWriteTime -ge $f.LastWriteTime) { $count++; continue }
    }
    try {
        $img = [System.Drawing.Image]::FromFile($f.FullName)
        $srcRatio = $img.Width / $img.Height
        if ($srcRatio -gt $targetRatio) {
            $cropH = $img.Height
            $cropW = [int]($cropH * $targetRatio)
        } else {
            $cropW = $img.Width
            $cropH = [int]($cropW / $targetRatio)
        }
        $x = [int](($img.Width - $cropW) / 2)
        $y = [int](($img.Height - $cropH) / 2)
        $bmp = New-Object System.Drawing.Bitmap($Width, $targetH)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($img, (New-Object System.Drawing.Rectangle(0, 0, $Width, $targetH)),
            (New-Object System.Drawing.Rectangle($x, $y, $cropW, $cropH)),
            [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
        $bmp.Dispose()
        $img.Dispose()
        $count++
        Write-Host "  Generated: $($f.Name) (${Width}x${targetH})"
    } catch {
        Write-Host "  FAILED: $($f.Name) - $_"
    }
}
Write-Host "Done: $count thumbnails generated in $dst"
