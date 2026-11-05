<#
.SYNOPSIS
    Draws the Speed Budget app icon (a pair of gold coins, the front one marked R, on a green tile) and writes
    src/SpeedBudget.App/Assets/AppIcon.ico and AppIcon-256.png.
.DESCRIPTION
    The icon is drawn in code at every size, so small sizes stay crisp. Run it again after changing the drawing.
#>
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src/SpeedBudget.App/Assets'

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Color([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $size / 100.0

    # Tile: green squircle with a soft top-to-bottom gradient
    $tile = New-RoundedPath (4 * $u) (4 * $u) (92 * $u) (92 * $u) (22 * $u)
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.PointF]::new(0, 4 * $u), [System.Drawing.PointF]::new(0, 96 * $u), (Color '#22C58B'), (Color '#0B7A5C'))
    $g.FillPath($grad, $tile)

    # A second coin behind the first, so it reads as money rather than a single badge
    $back = New-Object System.Drawing.SolidBrush (Color '#C98A12')
    $g.FillEllipse($back, (14 * $u), (30 * $u), (50 * $u), (50 * $u))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color '#F2B72A')), (14 * $u), (27 * $u), (50 * $u), (50 * $u))

    # The main coin: a gold disc with a raised rim and a bold R (the rand sign)
    $rim = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.PointF]::new(0, 18 * $u), [System.Drawing.PointF]::new(0, 86 * $u), (Color '#FFE27A'), (Color '#E2A417'))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color '#9A6A0A')), (30 * $u), (23 * $u), (58 * $u), (58 * $u))
    $g.FillEllipse($rim, (30 * $u), (19 * $u), (58 * $u), (58 * $u))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color '#F7C640')), (35.5 * $u), (24.5 * $u), (47 * $u), (47 * $u))
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float](34 * $u)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.DrawString('R', $font, (New-Object System.Drawing.SolidBrush (Color '#8A5A00')), [System.Drawing.RectangleF]::new(35.5 * $u, 24.5 * $u, 47 * $u, 49 * $u), $fmt)

    $g.Dispose()
    return $bmp
}

function Png-Bytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($s in $sizes) { , @($s, (Png-Bytes (Draw-Icon $s))) }

# Write the .ico: a header, one directory entry per size, then each PNG image
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
    $s = $i[0]; $bytes = $i[1]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($i in $images) { $w.Write([byte[]]$i[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.ico'), $out.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon-256.png'), ($images | Where-Object { $_[0] -eq 256 })[1])
Write-Host "Wrote AppIcon.ico ($($sizes -join ', ')) and AppIcon-256.png to $assets"
