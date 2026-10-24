<#
Builds the pinyin table the app embeds (src\DeskWidgets.App\Assets\Pinyin\pinyin.bin) from the Unicode Han Database (Unihan), for the
Big Folder's pinyin search (Phase 13.2).

The data is the Unihan database (Unihan.zip, from https://www.unicode.org/Public/UCD/latest/ucd/Unihan.zip), under the Unicode License v3
(see src\DeskWidgets.App\Assets\Pinyin\UNICODE-LICENSE.txt). Unzip it and pass Unihan_Readings.txt. Nothing else is used, and no pinyin
library or other project's data.

Usage: Make-PinyinTable.ps1 -Readings <path to Unihan_Readings.txt> [-Out <path to the .bin>]

Readings kept for a character: its kMandarin, kTGHZ2013 and kXHC1983 readings (the modern Mandarin ones, polyphonic characters included);
a character that has none of them takes its kHanyuPinyin readings. Each reading is turned into a toneless syllable: tone marks dropped,
u-diaeresis written as "v" (lv, nv), lower case.

Format (little endian), read by PinyinTable:
  "PYN1", int32 syllable count, then per syllable: byte length, ASCII letters;
  int32 entry count, then per entry (sorted by code point): varint code point minus the previous one, byte reading count,
  then that many uint16 syllable numbers.
#>
param(
    [Parameter(Mandatory)][string]$Readings,
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
if ($Out -eq '') { $Out = Join-Path (Split-Path $MyInvocation.MyCommand.Path) '..\src\DeskWidgets.App\Assets\Pinyin\pinyin.bin' }

function Normalize([string]$reading) {
    $decomposed = $reading.Normalize([System.Text.NormalizationForm]::FormD)
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $decomposed.Length; $i++) {
        $c = $decomposed[$i]
        $cat = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c)
        if ($cat -eq [System.Globalization.UnicodeCategory]::NonSpacingMark) { continue }
        if ($c -eq 'ü' -or $c -eq 'Ü') { [void]$sb.Append('v'); continue }
        if (($c -eq 'u' -or $c -eq 'U') -and $i + 1 -lt $decomposed.Length -and $decomposed[$i + 1] -eq [char]0x0308) { [void]$sb.Append('v'); continue }
        [void]$sb.Append([char]::ToLowerInvariant($c))
    }
    return $sb.ToString()
}

$primary = @{}   # code -> set of syllables from kMandarin, kTGHZ2013, kXHC1983
$fallback = @{}  # code -> set from kHanyuPinyin
foreach ($line in [System.IO.File]::ReadLines($Readings)) {
    if ($line.Length -lt 8 -or $line[0] -eq '#') { continue }
    $parts = $line.Split("`t")
    if ($parts.Length -lt 3) { continue }
    $field = $parts[1]
    if ($field -ne 'kMandarin' -and $field -ne 'kTGHZ2013' -and $field -ne 'kXHC1983' -and $field -ne 'kHanyuPinyin') { continue }
    $code = [Convert]::ToInt32($parts[0].Substring(2), 16)
    $target = if ($field -eq 'kHanyuPinyin') { $fallback } else { $primary }
    if (-not $target.ContainsKey($code)) { $target[$code] = New-Object 'System.Collections.Generic.List[string]' }
    foreach ($entry in $parts[2].Split(' ')) {
        if ($entry.Length -eq 0) { continue }
        # kMandarin: "bì"; the others: "0257.020:dōng" or, for kHanyuPinyin, "20811.060:háng,xìng".
        $list = if ($field -eq 'kMandarin') { $entry } else { $entry.Substring($entry.IndexOf(':') + 1) }
        foreach ($reading in $list.Split(',')) {
            $syllable = Normalize $reading
            if ($syllable -match '^[a-z]{1,7}$' -and -not $target[$code].Contains($syllable)) { $target[$code].Add($syllable) }
        }
    }
}

$final = New-Object 'System.Collections.Generic.SortedDictionary[int,object]'
foreach ($code in ($primary.Keys + $fallback.Keys | Sort-Object -Unique)) {
    $set = if ($primary.ContainsKey($code) -and $primary[$code].Count -gt 0) { $primary[$code] } else { $fallback[$code] }
    if ($null -ne $set -and $set.Count -gt 0) { $final[$code] = $set }
}

$syllables = @($final.Values | ForEach-Object { $_ } | Sort-Object -Unique)
$index = @{}
for ($i = 0; $i -lt $syllables.Count; $i++) { $index[$syllables[$i]] = $i }

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([System.Text.Encoding]::ASCII.GetBytes('PYN1'))
$writer.Write([int]$syllables.Count)
foreach ($s in $syllables) { $writer.Write([byte]$s.Length); $writer.Write([System.Text.Encoding]::ASCII.GetBytes($s)) }
$writer.Write([int]$final.Count)
$previous = 0
foreach ($code in @($final.Keys)) {
    $delta = $code - $previous
    $previous = $code
    while ($delta -ge 0x80) { $writer.Write([byte](($delta -band 0x7F) -bor 0x80)); $delta = $delta -shr 7 }
    $writer.Write([byte]$delta)
    $writer.Write([byte]$final[$code].Count)
    foreach ($s in $final[$code]) { $writer.Write([uint16]$index[$s]) }
}
$writer.Flush()
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
[System.IO.File]::WriteAllBytes($Out, $stream.ToArray())
"{0} characters, {1} syllables, {2} bytes -> {3}" -f $final.Count, $syllables.Count, $stream.Length, $Out
