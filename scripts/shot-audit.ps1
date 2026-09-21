# Rendered-text contrast auditor for GUI screenshots.
#
# It checks the one thing a screenshot can prove and a palette table cannot:
# for every pixel where the application painted a text colour, is that colour
# readable against whatever is immediately behind it?
#
# The background is taken from the neighbours of each glyph pixel rather than
# from the block, so a dark glyph on a small filled badge is judged against the
# badge fill, not against the card behind it. Decorative fills, hairlines and
# gradients never enter the check because only palette text colours are traced.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Path,
    [double]$MinContrast = 4.0,
    [int]$MinGlyphPixels = 40,
    [int]$Step = 2,
    [int]$ChromeTop = 118,
    [int]$ChromeSide = 10,
    [int]$TopN = 12
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-Lum([int]$r, [int]$g, [int]$b) {
    $f = {
        param($v)
        $s = $v / 255.0
        if ($s -le 0.03928) { return $s / 12.92 }
        return [Math]::Pow((($s + 0.055) / 1.055), 2.4)
    }
    return 0.2126 * (& $f $r) + 0.7152 * (& $f $g) + 0.0722 * (& $f $b)
}

function Get-Contrast([double]$la, [double]$lb) {
    if ($la -lt $lb) { $t = $la; $la = $lb; $lb = $t }
    return ($la + 0.05) / ($lb + 0.05)
}

# Every colour the application uses to draw glyphs (see Design/Palette.cs).
$textColors = @(
    'E9F0F7', # TextPrimary
    'AEC0D1', # TextSecondary
    '94A6B8', # TextMuted
    '6E8093', # TextDisabled
    'E3B25C', # Accent
    'F2C878', # AccentHover
    'C7953F', # AccentPressed
    '4FC98A', # Success
    'E5605A', # Danger
    'F0786F', # DangerHover
    '5FA8E8', # Info
    'F2F7FF', # SelectionText
    '171207'  # TextOnAccent
)
$textLum = @{}
foreach ($hex in $textColors) {
    $c = [System.Drawing.ColorTranslator]::FromHtml('#' + $hex)
    $textLum[$hex] = Get-Lum $c.R $c.G $c.B
}

$totalFlags = 0
foreach ($file in $Path) {
    $bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $file))
    Write-Host "`n===== $([System.IO.Path]::GetFileName($file))  $($bmp.Width) x $($bmp.Height) ====="

    # ---- global colour profile ------------------------------------------
    # This also yields the set of "surface" colours: colours that cover a real
    # area of the window. Glyph antialiasing produces hundreds of near-miss
    # blends, each individually rare, and they must never be mistaken for the
    # background a glyph sits on.
    $hist = @{}
    $light = 0
    $sampled = 0
    for ($y = 0; $y -lt $bmp.Height; $y += 2) {
        for ($x = 0; $x -lt $bmp.Width; $x += 2) {
            $c = $bmp.GetPixel($x, $y)
            $k = '{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B
            if ($hist.ContainsKey($k)) { $hist[$k]++ } else { $hist[$k] = 1 }
            $sampled++
            if ((Get-Lum $c.R $c.G $c.B) -gt 0.5) { $light++ }
        }
    }
    Write-Host ("  light pixels (lum>0.5): {0:N3}%   distinct colours: {1}" -f (100.0 * $light / $sampled), $hist.Count)

    $minSurface = [Math]::Max(200, [int]($sampled * 0.0004))
    $surfaceSet = @{}
    foreach ($e in $hist.GetEnumerator()) {
        if ($e.Value -ge $minSurface) { $surfaceSet[$e.Key] = $true }
    }
    Write-Host "  surface colours (>= $minSurface sampled px): $($surfaceSet.Count)"

    # ---- trace every glyph pixel to the background behind it -------------
    # pairTally["<text>|<bg>"] = number of glyph pixels sitting on that bg
    $pairTally = @{}
    $pairSample = @{}
    $glyphTotal = 0
    $yStart = [Math]::Max(1, $ChromeTop)
    $yEnd = $bmp.Height - [Math]::Max(1, $ChromeSide)
    $xStart = [Math]::Max(1, $ChromeSide)
    $xEnd = $bmp.Width - [Math]::Max(1, $ChromeSide)

    for ($y = $yStart; $y -lt $yEnd; $y += $Step) {
        for ($x = $xStart; $x -lt $xEnd; $x += $Step) {
            $c = $bmp.GetPixel($x, $y)
            $k = '{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B
            if ($textColors -notcontains $k) { continue }
            $glyphTotal++

            $nb = @{}
            foreach ($d in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1))) {
                $nc = $bmp.GetPixel($x + $d[0], $y + $d[1])
                $nk = '{0:X2}{1:X2}{2:X2}' -f $nc.R, $nc.G, $nc.B
                if (-not $surfaceSet.ContainsKey($nk)) { continue }
                if ($nb.ContainsKey($nk)) { $nb[$nk]++ } else { $nb[$nk] = 1 }
            }
            if ($nb.Count -eq 0) { continue }
            $bgKey = ($nb.GetEnumerator() | Sort-Object Value -Descending)[0].Key
            $pair = "$k|$bgKey"
            if ($pairTally.ContainsKey($pair)) { $pairTally[$pair]++ } else { $pairTally[$pair] = 1; $pairSample[$pair] = "$x,$y" }
        }
    }

    Write-Host "  glyph pixels traced: $glyphTotal"

    # ---- judge each (text, background) pair -----------------------------
    $flags = New-Object System.Collections.ArrayList
    $rows = New-Object System.Collections.ArrayList
    foreach ($pair in $pairTally.Keys) {
        if ($pairTally[$pair] -lt $MinGlyphPixels) { continue }
        $parts = $pair.Split('|')
        $textHex = $parts[0]; $bgHex = $parts[1]
        # A glyph pixel whose neighbour is the same colour is just a thick
        # stroke, not a contrast problem.
        if ($textHex -eq $bgHex) { continue }
        $ct = Get-Contrast $textLum[$textHex] (Get-Lum ([System.Drawing.ColorTranslator]::FromHtml('#' + $bgHex)).R ([System.Drawing.ColorTranslator]::FromHtml('#' + $bgHex)).G ([System.Drawing.ColorTranslator]::FromHtml('#' + $bgHex)).B)
        [void]$rows.Add([pscustomobject]@{ Contrast = $ct; Text = $textHex; Bg = $bgHex; Pixels = $pairTally[$pair]; Sample = $pairSample[$pair] })
        if ($ct -lt $MinContrast) {
            [void]$flags.Add(('text=#{0} on bg=#{1}  contrast={2:N2}:1  glyph px={3}  first at ({4})' -f $textHex, $bgHex, $ct, $pairTally[$pair], $pairSample[$pair]))
        }
    }

    Write-Host "  distinct text/background pairs: $($rows.Count)"
    $rows | Sort-Object Contrast | Select-Object -First $TopN | ForEach-Object {
        Write-Host ('    {0,6:N2}:1  text=#{1}  on=#{2}  px={3}' -f $_.Contrast, $_.Text, $_.Bg, $_.Pixels)
    }

    if ($flags.Count -eq 0) {
        Write-Host "  RESULT: OK - every drawn text colour reaches $($MinContrast):1 against its background"
    } else {
        Write-Host "  RESULT: $($flags.Count) UNREADABLE TEXT PLACEMENTS"
        $flags | Sort-Object | ForEach-Object { Write-Host "    $_" }
    }
    $totalFlags += $flags.Count
    $bmp.Dispose()
}

Write-Host "`nTOTAL unreadable text placements: $totalFlags"
if ($totalFlags -gt 0) { exit 2 }
exit 0
