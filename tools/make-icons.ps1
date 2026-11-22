# Renders the app icon in every size into src/Konjugieren/wwwroot/icons and src/Konjugieren/wwwroot/favicon.ico:
# a cream serif "K" on Konjugieren blue with three conjugation-table rows beside it, the middle one
# red like an irregular form. Favicons, too small for three rows, show two.
#   powershell -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\Konjugieren\wwwroot\icons"
New-Item -ItemType Directory -Force $out | Out-Null

$cream = [System.Drawing.Color]::FromArgb(0xFA, 0xF6, 0xEC)
$red = [System.Drawing.Color]::FromArgb(0xE5, 0x4B, 0x3C)

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

# $scale is the artwork's width as a share of the icon; maskable icons keep it inside the safe zone.
# $rounded gives the background its own rounded corners (favicons and manifest icons); iOS and maskable icons are rounded by the OS.
# $small draws two rows instead of three, for favicons.
function New-Icon([int]$size, [double]$scale = 0.78, [bool]$rounded = $false, [bool]$small = $false) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.TextRenderingHint = "AntiAliasGridFit"
    $g.InterpolationMode = "HighQualityBicubic"
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(0x34, 0x62, 0xA4)), ([System.Drawing.Color]::FromArgb(0x18, 0x31, 0x5A)), 60
    if ($rounded) {
        $g.FillPath($bg, (New-RoundedRect 0 0 $size $size ($size * 0.22)))
    } else {
        $g.FillRectangle($bg, $rect)
    }

    # The artwork lives in a square of side $a centred in the icon.
    $a = $size * $scale
    $x0 = ($size - $a) / 2
    $y0 = ($size - $a) / 2

    # Serif "K", shifted left so the rows fit beside it.
    $font = New-Object System.Drawing.Font "Georgia", ([float]($a * 0.8)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = "Center"
    $format.LineAlignment = "Center"
    $cRect = New-Object System.Drawing.RectangleF ($x0 - $a * 0.17), ($y0 + $a * 0.02), $a, $a
    $g.DrawString("K", $font, (New-Object System.Drawing.SolidBrush $cream), $cRect, $format)

    # Table rows to the right of the K.
    $rowX = $x0 + $a * 0.66
    $rowW = $a * 0.37
    $rowH = [Math]::Max(2, $a * 0.08)
    if ($small) {
        $rows = @(@{ Y = 0.38; Color = $cream; W = 1.0 }, @{ Y = 0.55; Color = $red; W = 0.85 })
        $rowH = [Math]::Max(2, $a * 0.11)
    } else {
        $rows = @(@{ Y = 0.37; Color = $cream; W = 1.0 }, @{ Y = 0.48; Color = $red; W = 0.8 }, @{ Y = 0.59; Color = $cream; W = 0.9 })
    }
    foreach ($row in $rows) {
        $w = $rowW * $row.W
        $path = New-RoundedRect $rowX ($y0 + $a * $row.Y) $w $rowH ($rowH / 2)
        $g.FillPath((New-Object System.Drawing.SolidBrush $row.Color), $path)
    }

    $g.Dispose()
    $bmp
}

function Save-Png($bmp, [string]$name) {
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# iOS home screen (opaque, square: iOS rounds the corners itself).
Save-Png (New-Icon 180) "apple-touch-icon.png"
Save-Png (New-Icon 167) "apple-touch-icon-167.png"
Save-Png (New-Icon 152) "apple-touch-icon-152.png"

# Web app manifest (Windows/Android install) and a big master copy.
Save-Png (New-Icon 1024) "icon-1024.png"
Save-Png (New-Icon 512 0.78 $true) "icon-512.png"
Save-Png (New-Icon 192 0.78 $true) "icon-192.png"
Save-Png (New-Icon 512 0.6) "icon-512-maskable.png"

# Browser tab: bigger artwork and two rows so it reads at 16 px.
Save-Png (New-Icon 32 0.92 $true $true) "favicon-32.png"

# favicon.ico with 16, 32 and 48 px PNG images, for Windows and older browsers.
$images = foreach ($s in 16, 32, 48) {
    $bmp = New-Icon $s 0.92 $true $true
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}
$ico = New-Object System.IO.MemoryStream
$wr = New-Object System.IO.BinaryWriter $ico
$wr.Write([uint16]0); $wr.Write([uint16]1); $wr.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $wr.Write([byte]$img.Size); $wr.Write([byte]$img.Size); $wr.Write([byte]0); $wr.Write([byte]0)
    $wr.Write([uint16]1); $wr.Write([uint16]32)
    $wr.Write([uint32]$img.Bytes.Length); $wr.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $wr.Write($img.Bytes) }
$wr.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $out "..\favicon.ico"), $ico.ToArray())

Write-Host "Icons written to $out"
