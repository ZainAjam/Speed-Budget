<#
.SYNOPSIS
    Draws the Speed Budget app icon (a wallet with a banknote on a green tile) and writes
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

    # Banknote peeking out of the wallet, tilted a little
    $g.TranslateTransform(50 * $u, 40 * $u)
    $g.RotateTransform(-9)
    $note = New-RoundedPath (-27 * $u) (-19 * $u) (54 * $u) (34 * $u) (4 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush (Color '#E8FFF4')), $note)
    $g.DrawPath(([System.Drawing.Pen]::new((Color '#9FE3C4'), [float][Math]::Max(1, 1.6 * $u))), $note)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color '#5CCB9B')), (-8 * $u), (-12 * $u), (16 * $u), (16 * $u))
    $g.ResetTransform()

    # Wallet body
    $body = New-RoundedPath (17 * $u) (38 * $u) (66 * $u) (44 * $u) (8 * $u)
    $bodyBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.PointF]::new(0, 38 * $u), [System.Drawing.PointF]::new(0, 82 * $u), (Color '#FFFFFF'), (Color '#D5F5E7'))
    $g.FillPath($bodyBrush, $body)

    # Clasp tab on the right with its button
    $clasp = New-RoundedPath (62 * $u) (53 * $u) (28 * $u) (16 * $u) (8 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush (Color '#0E8E6A')), $clasp)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color '#FFFFFF')), (68.5 * $u), (57.5 * $u), (7 * $u), (7 * $u))

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
