<#
Packs Fusion Pixel Font 8 px (Simplified Chinese, proportional) into the compact table the app embeds:
src\DeskWidgets.App\Assets\Fonts\FusionPixel8-zh_hans.bin.

The font is Fusion Pixel Font by TakWolf (https://github.com/TakWolf/fusion-pixel-font), release 2026.10.16, under the SIL OFL 1.1
(see src\DeskWidgets.App\Assets\Fonts\OFL-FusionPixel.txt). Get fusion-pixel-8px-proportional-zh_hans.bdf from that release.

Usage: Make-FusionFont.ps1 -Bdf <path to the .bdf> [-Out <path to the .bin>]

Format (little endian), read by FusionGlyphs:
  "FUS8", int32 glyph count, then per glyph:
  uint16 code, byte advance, byte width, byte height, sbyte x, sbyte y (BDF BBX offsets), then height rows of ceil(width / 8) bytes, MSB first.
Only BMP characters are kept; the private-use area (E000-F8FF) is left out because the app uses it for emoji placeholders.
#>
param(
    [Parameter(Mandatory)][string]$Bdf,
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
if ($Out -eq '') { $Out = Join-Path (Split-Path $MyInvocation.MyCommand.Path) '..\src\DeskWidgets.App\Assets\Fonts\FusionPixel8-zh_hans.bin' }

$glyphs = [System.Collections.Generic.List[byte[]]]::new()
$lines = [System.IO.File]::ReadAllLines($Bdf)
$i = 0
while ($i -lt $lines.Length) {
    if (-not $lines[$i].StartsWith('ENCODING ')) { $i++; continue }
    $code = [int]$lines[$i].Substring(9)
    $adv = 0; $w = 0; $h = 0; $x = 0; $y = 0
    $j = $i + 1
    while (-not $lines[$j].StartsWith('BITMAP')) {
        $p = $lines[$j].Split(' ')
        if ($p[0] -eq 'DWIDTH') { $adv = [int]$p[1] }
        elseif ($p[0] -eq 'BBX') { $w = [int]$p[1]; $h = [int]$p[2]; $x = [int]$p[3]; $y = [int]$p[4] }
        $j++
    }
    $rowBytes = [int][math]::Ceiling($w / 8)
    $keep = $code -ge 0 -and $code -le 0xFFFF -and -not ($code -ge 0xE000 -and $code -le 0xF8FF) -and $adv -gt 0
    if ($keep) {
        $bytes = [System.Collections.Generic.List[byte]]::new()
        $bytes.Add([byte]($code -band 0xFF)); $bytes.Add([byte]($code -shr 8))
        $bytes.Add([byte]$adv); $bytes.Add([byte]$w); $bytes.Add([byte]$h)
        $bytes.Add([byte]($x -band 0xFF)); $bytes.Add([byte]($y -band 0xFF))
        for ($r = 0; $r -lt $h; $r++) {
            $hex = $lines[$j + 1 + $r]
            for ($b = 0; $b -lt $rowBytes; $b++) {
                $bytes.Add([System.Convert]::ToByte($hex.Substring($b * 2, 2), 16))
            }
        }
        $glyphs.Add($bytes.ToArray())
    }
    $i = $j + $h + 1
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$stream = [System.IO.File]::Create($Out)
try {
    $stream.Write([System.Text.Encoding]::ASCII.GetBytes('FUS8'), 0, 4)
    $stream.Write([System.BitConverter]::GetBytes([int]$glyphs.Count), 0, 4)
    foreach ($g in $glyphs) { $stream.Write($g, 0, $g.Length) }
}
finally { $stream.Dispose() }
"{0} glyphs, {1} bytes -> {2}" -f $glyphs.Count, (Get-Item $Out).Length, $Out
