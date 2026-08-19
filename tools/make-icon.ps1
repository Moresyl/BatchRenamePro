# Renders the application icon into a multi-resolution .ico plus a 256px .png.
#
# The shapes are rasterised from analytic distance fields rather than drawn through WPF or GDI+,
# so the script produces byte-identical output on any machine and needs no display, no GPU and no
# STA thread — which matters because it runs in CI.
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\src\BatchRenamePro.App\Assets")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

# ---------------------------------------------------------------- geometry, on a 256x256 canvas

# Signed distance to a rounded rectangle: negative inside, positive outside.
function Get-RoundedRectDistance([double] $X, [double] $Y, [double] $Left, [double] $Top, [double] $Width, [double] $Height, [double] $Radius) {
    $halfWidth = $Width / 2
    $halfHeight = $Height / 2
    $dx = [Math]::Abs($X - ($Left + $halfWidth)) - ($halfWidth - $Radius)
    $dy = [Math]::Abs($Y - ($Top + $halfHeight)) - ($halfHeight - $Radius)
    $outsideX = [Math]::Max($dx, 0.0)
    $outsideY = [Math]::Max($dy, 0.0)
    $outside = [Math]::Sqrt($outsideX * $outsideX + $outsideY * $outsideY)
    $inside = [Math]::Min([Math]::Max($dx, $dy), 0.0)
    return $outside + $inside - $Radius
}

# Signed distance to a round-capped line segment.
function Get-SegmentDistance([double] $X, [double] $Y, [double] $X1, [double] $Y1, [double] $X2, [double] $Y2, [double] $HalfWidth) {
    $vx = $X2 - $X1
    $vy = $Y2 - $Y1
    $wx = $X - $X1
    $wy = $Y - $Y1
    $lengthSquared = $vx * $vx + $vy * $vy
    $t = if ($lengthSquared -le 0) { 0.0 } else { [Math]::Clamp(($wx * $vx + $wy * $vy) / $lengthSquared, 0.0, 1.0) }
    $dx = $wx - $t * $vx
    $dy = $wy - $t * $vy
    return [Math]::Sqrt($dx * $dx + $dy * $dy) - $HalfWidth
}

$bars = @(
    @{ Left = 54.0; Top = 54.0; Width = 104.0; Height = 24.0; Alpha = 0.42 },
    @{ Left = 54.0; Top = 96.0; Width = 148.0; Height = 24.0; Alpha = 0.95 },
    @{ Left = 54.0; Top = 138.0; Width = 104.0; Height = 24.0; Alpha = 0.42 }
)

$arrow = @(
    @{ X1 = 142.0; Y1 = 197.0; X2 = 200.0; Y2 = 197.0 },
    @{ X1 = 179.0; Y1 = 176.0; X2 = 200.0; Y2 = 197.0 },
    @{ X1 = 179.0; Y1 = 218.0; X2 = 200.0; Y2 = 197.0 }
)

# ------------------------------------------------------------------------------- rasterisation

function New-IconPixels([int] $Size) {
    $pixels = New-Object 'byte[]' ($Size * $Size * 4)
    $scale = 256.0 / $Size
    $samples = 4                      # 4x4 supersampling; enough for shapes this simple
    $step = 1.0 / $samples
    $weight = 1.0 / ($samples * $samples)
    # Anti-alias over roughly one destination pixel, expressed in canvas units.
    $feather = $scale * 0.75

    for ($py = 0; $py -lt $Size; $py++) {
        for ($px = 0; $px -lt $Size; $px++) {
            $red = 0.0; $green = 0.0; $blue = 0.0; $alpha = 0.0

            for ($sy = 0; $sy -lt $samples; $sy++) {
                for ($sx = 0; $sx -lt $samples; $sx++) {
                    $x = ($px + ($sx + 0.5) * $step) * $scale
                    $y = ($py + ($sy + 0.5) * $step) * $scale

                    $plate = 1.0 - [Math]::Clamp((Get-RoundedRectDistance $x $y 8 8 240 240 56) / $feather + 0.5, 0.0, 1.0)
                    if ($plate -le 0) { continue }

                    # Diagonal indigo-to-violet gradient across the plate.
                    $t = [Math]::Clamp(($x + $y - 16) / 480.0, 0.0, 1.0)
                    $sampleRed = 79 + (151 - 79) * $t
                    $sampleGreen = 110 + (86 - 110) * $t
                    $sampleBlue = 247 + (246 - 247) * $t

                    # White marks composited over the plate, all of them clipped to it.
                    $ink = 0.0
                    foreach ($bar in $bars) {
                        $coverage = 1.0 - [Math]::Clamp((Get-RoundedRectDistance $x $y $bar.Left $bar.Top $bar.Width $bar.Height 12) / $feather + 0.5, 0.0, 1.0)
                        $ink = [Math]::Max($ink, $coverage * $bar.Alpha)
                    }
                    foreach ($segment in $arrow) {
                        $coverage = 1.0 - [Math]::Clamp((Get-SegmentDistance $x $y $segment.X1 $segment.Y1 $segment.X2 $segment.Y2 8) / $feather + 0.5, 0.0, 1.0)
                        $ink = [Math]::Max($ink, $coverage * 0.95)
                    }

                    $sampleRed = $sampleRed + (255 - $sampleRed) * $ink
                    $sampleGreen = $sampleGreen + (255 - $sampleGreen) * $ink
                    $sampleBlue = $sampleBlue + (255 - $sampleBlue) * $ink

                    $red += $sampleRed * $plate * $weight
                    $green += $sampleGreen * $plate * $weight
                    $blue += $sampleBlue * $plate * $weight
                    $alpha += $plate * $weight
                }
            }

            $index = ($py * $Size + $px) * 4
            if ($alpha -gt 0.0009) {
                # Straight (un-premultiplied) RGBA, which is what both PNG and .ico expect.
                $pixels[$index] = [byte][Math]::Clamp([Math]::Round($red / $alpha), 0, 255)
                $pixels[$index + 1] = [byte][Math]::Clamp([Math]::Round($green / $alpha), 0, 255)
                $pixels[$index + 2] = [byte][Math]::Clamp([Math]::Round($blue / $alpha), 0, 255)
                $pixels[$index + 3] = [byte][Math]::Clamp([Math]::Round($alpha * 255), 0, 255)
            }
        }
    }

    return , $pixels
}

# ------------------------------------------------------------------------------- PNG encoding

# Written in decimal: PowerShell reads 0xEDB88320 as a *signed* Int32, which will not cast to uint32.
$crcPolynomial = [uint32] 3988292384      # 0xEDB88320
$crcSeed = [uint32] 4294967295            # 0xFFFFFFFF

$crcTable = New-Object 'uint32[]' 256
for ($n = 0; $n -lt 256; $n++) {
    [uint32] $c = $n
    for ($k = 0; $k -lt 8; $k++) {
        $c = if ($c -band 1) { $crcPolynomial -bxor ($c -shr 1) } else { $c -shr 1 }
    }
    $crcTable[$n] = $c
}

function Get-Crc32([byte[]] $Bytes) {
    [uint32] $crc = $crcSeed
    foreach ($b in $Bytes) {
        $crc = $crcTable[[int](($crc -bxor $b) -band 0xFF)] -bxor ($crc -shr 8)
    }
    return [uint32] ($crc -bxor $crcSeed)
}

function Get-BigEndian([uint32] $Value) {
    return [byte[]] @([byte](($Value -shr 24) -band 0xFF), [byte](($Value -shr 16) -band 0xFF), [byte](($Value -shr 8) -band 0xFF), [byte]($Value -band 0xFF))
}

function New-PngChunk([string] $Type, [byte[]] $Data) {
    $typeBytes = [System.Text.Encoding]::ASCII.GetBytes($Type)
    $body = $typeBytes + $Data
    return (Get-BigEndian ([uint32]$Data.Length)) + $body + (Get-BigEndian (Get-Crc32 $body))
}

function ConvertTo-Png([byte[]] $Pixels, [int] $Size) {
    # PNG wants a filter byte in front of every scanline; filter 0 (none) keeps this simple and
    # still compresses well, because the artwork is large areas of flat colour.
    $raw = New-Object 'byte[]' (($Size * 4 + 1) * $Size)
    for ($y = 0; $y -lt $Size; $y++) {
        $destination = $y * ($Size * 4 + 1) + 1
        [Array]::Copy($Pixels, $y * $Size * 4, $raw, $destination, $Size * 4)
    }

    $buffer = [System.IO.MemoryStream]::new()
    $deflate = [System.IO.Compression.ZLibStream]::new($buffer, [System.IO.Compression.CompressionLevel]::SmallestSize, $true)
    $deflate.Write($raw, 0, $raw.Length)
    $deflate.Dispose()
    $compressed = $buffer.ToArray()
    $buffer.Dispose()

    $header = (Get-BigEndian ([uint32]$Size)) + (Get-BigEndian ([uint32]$Size)) + [byte[]] @(8, 6, 0, 0, 0)

    $png = [byte[]] @(137, 80, 78, 71, 13, 10, 26, 10) +
        (New-PngChunk "IHDR" $header) +
        (New-PngChunk "IDAT" $compressed) +
        (New-PngChunk "IEND" @())

    # The unary comma keeps this a single byte[] instead of unrolling it into the pipeline,
    # which would hand callers an Object[] and send BinaryWriter.Write to the wrong overload.
    return , ([byte[]] $png)
}

# ------------------------------------------------------------------------------------- output

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $frames += , (ConvertTo-Png (New-IconPixels $size) $size)
}

[System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory "app-256.png"), $frames[$sizes.Count - 1])

# Vista-era .ico container: every frame is stored as a PNG payload.
$file = [System.IO.File]::Create((Join-Path $OutputDirectory "app.ico"))
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { [byte]0 } else { [byte]$sizes[$i] }
    $writer.Write($dimension)
    $writer.Write($dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame, 0, $frame.Length) }
$writer.Flush()
$writer.Dispose()
$file.Dispose()

for ($i = 0; $i -lt $sizes.Count; $i++) { "  {0,3}px  {1,7:N0} bytes" -f $sizes[$i], $frames[$i].Length }
"  app.ico  {0,7:N0} bytes" -f (Get-Item (Join-Path $OutputDirectory "app.ico")).Length
