param(
    [string]$OutputDir = "E:\Project\easy-hexo-by-monish\EasyHexo\Assets"
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $cx = $size / 2.0
    $cy = $size / 2.0
    $r = $size * 0.46

    # 蓝色六边形（Hexo 风格）
    $points = New-Object 'System.Drawing.PointF[]' 6
    for ($i = 0; $i -lt 6; $i++) {
        $angle = [Math]::PI / 180.0 * (60 * $i - 30)
        $points[$i] = New-Object System.Drawing.PointF (
            [float]($cx + $r * [Math]::Cos($angle)),
            [float]($cy + $r * [Math]::Sin($angle)))
    }

    $hexBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 14, 131, 205))
    $hexPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 8, 82, 130), [float][Math]::Max(1, $size * 0.03))
    $g.FillPolygon($hexBrush, $points)
    $g.DrawPolygon($hexPen, $points)

    # 白色 H
    $fontSize = [float]($size * 0.42)
    $font = New-Object System.Drawing.Font "Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF 0, 0, ([float]$size), ([float]$size)
    $g.DrawString("H", $font, $whiteBrush, $textRect, $format)

    # 右下角齿轮徽章
    $badge = $size * 0.36
    $bx = $size - $badge * 0.78
    $by = $size - $badge * 0.78
    $badgeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 11, 42, 59))
    $g.FillEllipse($badgeBrush, [float]($bx - $badge / 2), [float]($by - $badge / 2), [float]$badge, [float]$badge)

    $gr = $badge * 0.34
    $teeth = 8
    $gearPoints = New-Object 'System.Drawing.PointF[]' ($teeth * 4)
    for ($i = 0; $i -lt ($teeth * 4); $i++) {
        $angle = [Math]::PI / 180.0 * (360.0 / ($teeth * 4) * $i)
        $radius = if (($i % 4) -lt 2) { $gr } else { $gr * 0.72 }
        $gearPoints[$i] = New-Object System.Drawing.PointF (
            [float]($bx + $radius * [Math]::Cos($angle)),
            [float]($by + $radius * [Math]::Sin($angle)))
    }

    $gearBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPolygon($gearBrush, $gearPoints)
    $holeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 11, 42, 59))
    $hole = $gr * 0.44
    $g.FillEllipse($holeBrush, [float]($bx - $hole), [float]($by - $hole), [float]($hole * 2), [float]($hole * 2))

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @($size, $ms.ToArray())
    if ($size -eq 256) {
        $bmp.Save((Join-Path $OutputDir "EasyHexo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
    $ms.Dispose()
}

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $size = [int]$frame[0]
    $height = if ($size -ge 256) { 0 } else { $size }
    $width = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$width)
    $writer.Write([byte]$height)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame[1].Length)
    $writer.Write([uint32]$offset)
    $offset += $frame[1].Length
}

foreach ($frame in $frames) {
    $writer.Write([byte[]]$frame[1])
}

$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutputDir "EasyHexo.ico"), $stream.ToArray())
$writer.Dispose()
$stream.Dispose()
Write-Output ("icon written: " + (Join-Path $OutputDir "EasyHexo.ico"))
