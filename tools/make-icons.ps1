# Renders the app icon in every size into src/Konjugieren/wwwroot/icons and src/Konjugieren/wwwroot/favicon.ico:
# a cream serif "K" on the German flag (black, red, gold) with three conjugation-table rows beside it, the middle one
# red like an irregular form (gold where it would sit on red). The artwork is outlined so it reads on any band. Favicons, too small for three rows, show two.
#   powershell -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\Konjugieren\wwwroot\icons"
New-Item -ItemType Directory -Force $out | Out-Null

$cream = [System.Drawing.Color]::FromArgb(0xFA, 0xF6, 0xEC)
$red = [System.Drawing.Color]::FromArgb(0xE5, 0x4B, 0x3C)
$flagBlack = [System.Drawing.Color]::FromArgb(0x00, 0x00, 0x00)
$flagRed = [System.Drawing.Color]::FromArgb(0xDD, 0x00, 0x00)
$flagGold = [System.Drawing.Color]::FromArgb(0xFF, 0xCE, 0x00)
# The irregular row sits on the red band, so it is gold instead of red.
$irregular = $flagGold

# A thin dark outline keeps the cream artwork readable where it crosses a light part of the flag.
$outline = [System.Drawing.Color]::FromArgb(150, 0x1A, 0x10, 0x10)

function Add-OutlinedShape($g, $path, $color, [float]$width) {
    $pen = New-Object System.Drawing.Pen $outline, ([float][Math]::Max(1, $width))
    $pen.LineJoin = "Round"
    $g.DrawPath($pen, $path)
    $g.FillPath((New-Object System.Drawing.SolidBrush $color), $path)
}

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

    # Rounded icons (favicons and manifest icons) clip the flag to their own corners.
    if ($rounded) { $g.SetClip((New-RoundedRect 0 0 $size $size ($size * 0.22))) }
    # German flag: black, red and gold in three equal bands.
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $flagBlack), [float]0, [float]0, [float]$size, [float]($size / 3))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $flagRed), [float]0, [float]($size / 3), [float]$size, [float]($size / 3))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $flagGold), [float]0, [float]($size * 2 / 3), [float]$size, [float]($size / 3 + 1))
    $g.ResetClip()

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
    $glyph = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glyph.AddString("K", $font.FontFamily, [int]$font.Style, $font.Size, $cRect, $format)
    Add-OutlinedShape $g $glyph $cream ($a * 0.05)

    # Table rows to the right of the K.
    $rowX = $x0 + $a * 0.66
    $rowW = $a * 0.37
    $rowH = [Math]::Max(2, $a * 0.08)
    if ($small) {
        $rows = @(@{ Y = 0.38; Color = $cream; W = 1.0 }, @{ Y = 0.55; Color = $irregular; W = 0.85 })
        $rowH = [Math]::Max(2, $a * 0.11)
    } else {
        $rows = @(@{ Y = 0.37; Color = $cream; W = 1.0 }, @{ Y = 0.48; Color = $irregular; W = 0.8 }, @{ Y = 0.59; Color = $cream; W = 0.9 })
    }
    foreach ($row in $rows) {
        $w = $rowW * $row.W
        $path = New-RoundedRect $rowX ($y0 + $a * $row.Y) $w $rowH ($rowH / 2)
        Add-OutlinedShape $g $path $row.Color ($a * 0.03)
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
