# Generates src\DeskWidgets.App\Assets\AppIcon.ico: a dark glass tile with a
# green battery ring (and a mouse glyph at larger sizes), PNG-compressed entries.
# Run: powershell -ExecutionPolicy Bypass -File tools\Make-AppIcon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\DeskWidgets.App\Assets\AppIcon.ico'

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded tile
    $m = [Math]::Max(0.5, $size * 0.04)
    $rect = New-Object System.Drawing.RectangleF($m, $m, ($size - 2 * $m), ($size - 2 * $m))
    $d = $size * 0.46
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $tile.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $tile.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $tile.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 64, 64, 70), [System.Drawing.Color]::FromArgb(255, 22, 22, 26), 90.0)
    $g.FillPath($fill, $tile)

    # Battery ring, 75 %
    $thickness = [Math]::Max(1.6, $size * 0.105)
    $inset = $size * 0.21
    $ring = New-Object System.Drawing.RectangleF($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))
    $track = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(64, 255, 255, 255), $thickness)
    $g.DrawEllipse($track, $ring)
    $arc = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 48, 209, 88), $thickness)
    $arc.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arc.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($arc, $ring, -90, 270)

    if ($size -ge 40) {
        $font = New-Object System.Drawing.Font('Segoe Fluent Icons', [float]($size * 0.22), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $g.DrawString([string][char]0xE962, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 0, $size, $size)), $format)
    }
    $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $stream.ToArray()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) { $images.Add((New-IconPng $s)) }

$file = [System.IO.File]::Create($out)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0)              # reserved
$writer.Write([uint16]1)              # type: icon
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Dispose()
Write-Host "wrote $out ($((Get-Item $out).Length) bytes)"
