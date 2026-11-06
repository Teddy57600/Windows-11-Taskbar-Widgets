# Génère src/TaskbarWidgets/Assets/TaskbarWidgets.ico (16 → 256 px, entrées PNG).
# Motif : une mini barre des tâches sombre portant quatre jauges colorées (CPU, RAM, GPU, TEMP).
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\TaskbarWidgets\Assets"))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Min($r, [Math]::Min($w, $h) / 2)
    if ($r -le 0.01) { $p.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $p }
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function C([string]$hex, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B)
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.PixelOffsetMode = "HighQuality"
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $small = $size -le 24

    # Fond : carré arrondi, dégradé bleu nuit, léger reflet en haut.
    $m = if ($small) { 0 } else { 8 * $s }
    $bgRect = New-Object System.Drawing.RectangleF $m, $m, ($size - 2 * $m), ($size - 2 * $m)
    $bg = New-RoundedPath $m $m ($size - 2 * $m) ($size - 2 * $m) ($(if ($small) { 3 * $size / 16 } else { 56 * $s }))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bgRect, (C "#2B3F78"), (C "#0D1530"), 60.0
    $g.FillPath($brush, $bg)
    if (-not $small) {
        $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bgRect, (C "#FFFFFF" 40), (C "#FFFFFF" 0), 90.0
        $blend = New-Object System.Drawing.Drawing2D.Blend 3
        $blend.Factors = [float[]](0, 1, 1); $blend.Positions = [float[]](0, 0.45, 1)
        $shine.Blend = $blend
        $g.FillPath($shine, $bg)
        $g.DrawPath((New-Object System.Drawing.Pen (C "#FFFFFF" 38), ([Math]::Max(1, 3 * $s))), $bg)
    }

    # Jauges (couleurs des widgets) et leurs niveaux.
    $gauges = if ($small) {
        @(@("#4CC2FF", 0.55), @("#D89CF6", 0.85), @("#6CCB5F", 0.40))
    } else {
        @(@("#4CC2FF", 0.55), @("#D89CF6", 0.88), @("#6CCB5F", 0.35), @("#FF7B7B", 0.70))
    }
    $n = $gauges.Count
    $barW  = if ($small) { [Math]::Round($size * 0.17) } else { 26 * $s }
    $gap   = if ($small) { [Math]::Round($size * 0.11) } else { 22 * $s }
    $top   = if ($small) { $size * 0.16 } else { 46 * $s }
    $bot   = if ($small) { $size * 0.70 } else { 176 * $s }
    $total = $n * $barW + ($n - 1) * $gap
    $x0 = ($size - $total) / 2
    $trackH = $bot - $top

    for ($i = 0; $i -lt $n; $i++) {
        $x = $x0 + $i * ($barW + $gap)
        if ($small) { $x = [Math]::Round($x) }
        $r = $barW / 2
        if (-not $small) {
            $track = New-RoundedPath $x $top $barW $trackH $r
            $g.FillPath((New-Object System.Drawing.SolidBrush (C "#FFFFFF" 30)), $track)
        }
        $h = [Math]::Max($barW, $trackH * $gauges[$i][1])
        $fillRect = New-Object System.Drawing.RectangleF $x, ($bot - $h), $barW, $h
        $fill = New-RoundedPath $x ($bot - $h) $barW $h ($(if ($small) { 1 } else { $r }))
        $base = C $gauges[$i][0]
        $light = [System.Drawing.Color]::FromArgb(255, [Math]::Min(255, $base.R + 35), [Math]::Min(255, $base.G + 35), [Math]::Min(255, $base.B + 35))
        $gb = New-Object System.Drawing.Drawing2D.LinearGradientBrush $fillRect, $light, $base, 90.0
        $g.FillPath($gb, $fill)
    }

    # Barre des tâches stylisée sous les jauges.
    $tbY = if ($small) { $size * 0.80 } else { 196 * $s }
    $tbH = if ($small) { [Math]::Max(2, [Math]::Round($size * 0.12)) } else { 22 * $s }
    $tbX = if ($small) { $size * 0.14 } else { 46 * $s }
    $tb = New-RoundedPath $tbX $tbY ($size - 2 * $tbX) $tbH ($tbH / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush (C "#FFFFFF" ($(if ($small) { 200 } else { 215 })))), $tb)
    if (-not $small) {
        # Bouton Démarrer façon Windows 11 au centre de la barre.
        $cx = $size / 2; $cy = $tbY + $tbH / 2
        $sb = New-Object System.Drawing.SolidBrush (C "#2B6CF0")
        $u = 7 * $s; $sp = 1.6 * $s
        foreach ($ix in 0, 1) { foreach ($iy in 0, 1) {
            $g.FillPath($sb, (New-RoundedPath ($cx - $u - $sp / 2 + $ix * ($u + $sp)) ($cy - $u - $sp / 2 + $iy * ($u + $sp)) $u $u (1.5 * $s)))
        } }
    }

    $g.Dispose()
    return $bmp
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $bmp = Render $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir "TaskbarWidgets-256.png")) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$icoPath = Join-Path $OutDir "TaskbarWidgets.ico"
$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()
"Icône écrite : $icoPath"
